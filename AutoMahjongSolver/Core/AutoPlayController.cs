using AutoMahjongSolver.Core.Game;
using AutoMahjongSolver.Engine;
using Dalamud.Plugin.Services;

namespace AutoMahjongSolver.Core;

/// <summary>
/// Live cockpit driver with two modes (see <see cref="PlayMode"/>). In Auto it runs the autonomous play
/// loop: each of our turns it discards the engine's best tile, declares riichi or tsumo when live, and on
/// call popups accepts Ron, takes a pon when <see cref="CallPolicy"/> approves, or passes; it advances
/// result / new-hand screens. In Suggest it sends no inputs at all — the cockpit just shows the move and
/// the player makes it. Casual tables only; unattended automation of a PvP mode.
/// </summary>
internal sealed class AutoPlayController : IDisposable
{
    private const long TickIntervalMs = 120;

    /// <summary>Pause between firing the riichi option and cutting the riichi tile, letting the prompt close.</summary>
    private const int RiichiFollowupDelayMs = 450;

    /// <summary>
    /// Popup fires are verified by re-checking the state: a callback sent while the prompt is still
    /// animating in gets swallowed (seen live: the bot's [11, 1] pass was ignored, an identical manual
    /// click seconds later worked). Retry after this delay, give up after <see cref="MaxPopupAttempts"/>.
    /// </summary>
    private const int PopupRetryDelayMs = 700;
    private const int MaxPopupAttempts = 4;

    /// <summary>
    /// Hold all actuation this long after the Emj addon first appears (or reappears). Acting while
    /// the table is still dealing/animating wedged a match live: the state codes are readable well
    /// before the table actually accepts inputs.
    /// </summary>
    private const int MatchStartGraceMs = 6000;

    /// <summary>Warn once if an actionable state sits unresolved this long — surfaces silent stalls.</summary>
    private const int StuckWarnMs = 12000;

    private readonly Random rng = new();

    private bool running;
    private bool hooked;
    private long nextTickAllowed;
    private int prevState = int.MinValue;
    private long actAtTick;
    private bool acted;
    private bool riichiFired;
    private int popupAttempts;
    private bool addonAbsent = true;
    private long graceUntil;
    private long stateSince;
    private bool stuckWarned;

    private readonly int[] prevRiverLens = new int[4];
    private bool riversSeeded;
    private int lastDiscardTile = -1;

    /// <summary>The cockpit is engaged (showing live reads), in either mode.</summary>
    public bool Running => running;

    public PlayMode Mode => Plugin.Cfg.Mode;

    /// <summary>What the bot has done since the cockpit was last engaged.</summary>
    public SessionStats Stats { get; } = new();

    public void SetRunning(bool on)
    {
        if (on == running)
            return;
        running = on;

        if (on)
        {
            Stats.Reset();
            AmjsLog.Info(Mode == PlayMode.Auto
                ? "Auto-play ON (CASUAL tables only): discards, riichi, tsumo, ron, auto-pass, auto-advance, defense."
                : "Suggestions ON: showing the best move each turn. No inputs are sent for you.");
        }
        else
        {
            AmjsLog.Info("Cockpit OFF.");
        }

        UpdateHook();
    }

    public void SetMode(PlayMode mode)
    {
        if (Plugin.Cfg.Mode == mode)
            return;
        Plugin.Cfg.Mode = mode;
        Plugin.Cfg.Save();

        if (running)
            AmjsLog.Info(mode == PlayMode.Auto ? "Switched to Auto-play." : "Switched to Suggestions only.");

        UpdateHook();
    }

    /// <summary>The actuation loop only runs while engaged in Auto; Suggest mode leaves it unhooked.</summary>
    private void UpdateHook()
    {
        bool shouldHook = running && Mode == PlayMode.Auto;
        if (shouldHook == hooked)
            return;
        hooked = shouldHook;

        if (hooked)
        {
            prevState = int.MinValue;
            acted = false;
            addonAbsent = true; // even mid-match, take the settle grace before the first action
            graceUntil = 0;
            Plugin.Framework.Update += OnUpdate;
        }
        else
        {
            Plugin.Framework.Update -= OnUpdate;
        }
    }

    private void OnUpdate(IFramework framework)
    {
        long now = Environment.TickCount64;
        if (now < nextTickAllowed)
            return;
        nextTickAllowed = now + TickIntervalMs;

        int state = EmjBoardReader.ReadStateCode();

        if (state < 0)
        {
            if (!addonAbsent)
            {
                addonAbsent = true;
                prevState = int.MinValue;
                riversSeeded = false;
                lastDiscardTile = -1;
            }

            return;
        }

        if (addonAbsent)
        {
            addonAbsent = false;
            graceUntil = now + MatchStartGraceMs;
            AmjsLog.Info($"auto: table detected (state {state}); settling for {MatchStartGraceMs / 1000}s before acting.");
        }

        // prevState stays untouched during the grace so the first live tick re-arms cleanly.
        if (now < graceUntil)
            return;

        TrackDiscards();

        // 6 and 30 are the same "our turn" (our own [15] highlight moves 6->30 mid-discard); don't re-arm between them.
        bool sameTurn = state is 6 or 30 && prevState is 6 or 30;
        if (state != prevState && !sameTurn)
        {
            acted = false;
            riichiFired = false;
            popupAttempts = 0;
            actAtTick = now + DelayFor(state);
            stateSince = now;
            stuckWarned = false;
        }
        prevState = state;

        if (!stuckWarned && state is 2 or 6 or 15 or 28 or 30 && now - stateSince > StuckWarnMs)
        {
            stuckWarned = true;
            AmjsLog.Warn($"auto: state {state} unresolved for {(now - stateSince) / 1000}s (acted={acted}, popupAttempts={popupAttempts}).");
        }

        if (acted || now < actAtTick)
            return;

        switch (state)
        {
            case 6 or 30:
                DoTurn(afterCall: false);
                break;
            case 28:
                // Post-call discard surface: a tile must be cut, but no win/riichi is possible here.
                DoTurn(afterCall: true);
                break;
            case 15:
                DoCall();
                break;
            // State 2 (new-hand acknowledge) is intentionally NOT handled: the game fires its own
            // [9] when the deal animation finishes. Sending ours too caused a duplicate ack that
            // desynced the table (two live freezes both traced to a stray [9] ~3-4s after ours).
            case 29 or 32:
                if (EmjActuator.Advance())
                {
                    acted = true;
                    Stats.Record(AutoActionKind.Advance, "Advanced the table");
                }

                break;
        }
    }

    /// <summary>
    /// Watch the opponents' rivers grow to learn the most recent discard — the call popup itself
    /// does not say which tile is being offered. The first read after the addon appears only seeds
    /// the lengths (mid-hand engages would otherwise mistake the whole river for one discard).
    /// </summary>
    private void TrackDiscards()
    {
        BoardSnapshot? board = EmjBoardReader.TryReadBoard();
        if (board is null)
            return;

        for (int i = 0; i < prevRiverLens.Length && i < board.Rivers.Count; i++)
        {
            IReadOnlyList<int> river = board.Rivers[i];
            if (riversSeeded && i != AmjsConstants.SelfIndex && river.Count > prevRiverLens[i])
                lastDiscardTile = river[river.Count - 1];
            prevRiverLens[i] = river.Count;
        }

        riversSeeded = true;
    }

    private void DoTurn(bool afterCall)
    {
        if (!EmjBoardReader.TryReadHand(out HandSnapshot snap) || snap.TilesRead != 14 - 3 * snap.MeldCount)
            return; // wait until the full concealed draw is readable (melds remove three tiles each)

        if (!afterCall && ShantenCalculator.Calculate(snap.Counts, snap.MeldCount) == ShantenCalculator.Complete)
        {
            if (popupAttempts >= MaxPopupAttempts)
            {
                AmjsLog.Warn("turn: win panel did not accept the fire after retries; yielding to the game timer.");
                acted = true;
                return;
            }

            // The drawn-win (Tsumo/Pass) panel may carry no AtkValues labels (verified live); the
            // affirmative button is option 0 on every captured popup, so fall back to that.
            var actions = EmjBoardReader.ReadActionOptions();
            int tsumo = FindOption(actions, "Tsumo");
            int option = tsumo >= 0 ? tsumo : 0;

            if (EmjActuator.Call(option))
            {
                popupAttempts++;
                AmjsLog.Info($"turn: tsumo -> [11, {option}]{(tsumo < 0 ? " (unlabeled panel)" : "")} (attempt {popupAttempts}).");
                if (popupAttempts == 1)
                    Stats.Record(AutoActionKind.Tsumo, "Declared tsumo");
                actAtTick = Environment.TickCount64 + PopupRetryDelayMs; // verify next tick; re-fire if still up
            }

            return;
        }

        if (TryResolveNineTerminals(snap))
            return;

        BoardSnapshot? board = EmjBoardReader.TryReadBoard();

        // In riichi the hand is locked: the win/kan cases were handled above, so tsumogiri the draw.
        if (board?.OwnRiichi == true)
        {
            if (EmjActuator.DiscardSlot(13))
            {
                acted = true;
                Stats.Record(AutoActionKind.Discard, "Tsumogiri (riichi)");
            }
            return;
        }

        int[]? seen = board?.BuildSeen(snap.Counts, snap.Melds);
        int riichiOption = FindOption(EmjBoardReader.ReadActionOptions(), "Riichi");

        TurnDecision decision = TurnPolicy.Decide(new TurnInput
        {
            Counts14 = snap.Counts,
            Melds = snap.Melds,
            Seen = seen,
            Context = BuildEvalContext(snap, board),
            ThreatRivers = (IReadOnlyList<IReadOnlyList<int>>?)board?.ThreatRivers ?? Array.Empty<IReadOnlyList<int>>(),
            OwnRiver = board?.OwnRiver ?? Array.Empty<int>(),
            RiichiAvailable = riichiOption >= 0 && !riichiFired,
        });

        if (decision.Discard < 0)
            return;

        if (decision.Reason != "best value" && decision.Reason != "best value + riichi")
            AmjsLog.Info($"turn: {decision.Reason}.");

        if (decision.DeclareRiichi && EmjActuator.Call(riichiOption))
        {
            // The riichi tile is cut on the next eligible tick, once the prompt has closed.
            riichiFired = true;
            actAtTick = Environment.TickCount64 + RiichiFollowupDelayMs;
            AmjsLog.Info($"turn: riichi declared -> [11, {riichiOption}]; cutting {Tiles.Name(decision.Discard)} next tick.");
            Stats.Record(AutoActionKind.Riichi, "Declared riichi");
            return;
        }

        if (EmjActuator.DiscardTile(decision.Discard))
        {
            acted = true;
            Stats.Record(AutoActionKind.Discard, $"Discarded {Tiles.Name(decision.Discard)}");
        }
    }

    /// <summary>
    /// Table context for the value ranking. Seat wind and dealer status come from our own panel;
    /// the round wind is not readable yet, so East is assumed (correct for quick matches; a wrong
    /// guess only misweights one yakuhai pair).
    /// </summary>
    private static EvalContext BuildEvalContext(in HandSnapshot snap, BoardSnapshot? board)
    {
        int seatWind = board?.Panels[AmjsConstants.SelfIndex].Wind switch
        {
            "South" => 1,
            "West" => 2,
            "North" => 3,
            _ => 0,
        };

        return new EvalContext
        {
            SeatWind = seatWind,
            RoundWind = 0,
            Dealer = seatWind == 0,
            Closed = snap.MeldCount == 0,
            DoraIndicators = board?.DoraIndicators,
            AkaByType = snap.AkaByType,
        };
    }

    /// <summary>
    /// Answer the nine-terminals-and-honors (kyuushu kyuuhai) abort prompt. Default is to take the
    /// redeal — nine scattered terminals/honors is a 4-5 shanten hand. The exception is a strong
    /// kokushi start (thirteen-orphans shanten ≤ 2, i.e. 11+ types or 10 with a pair), which is a
    /// real yakuman attempt and worth keeping. Returns true while the prompt is being handled.
    /// </summary>
    private bool TryResolveNineTerminals(in HandSnapshot snap)
    {
        var actions = EmjBoardReader.ReadActionOptions();
        int abort = FindOption(actions, "Nine");
        if (abort < 0)
            return false;

        if (popupAttempts >= MaxPopupAttempts)
        {
            AmjsLog.Warn("turn: nine-terminals prompt did not accept the fire after retries; yielding to the game timer.");
            acted = true;
            return true;
        }

        int kokushiShanten = ShantenCalculator.ThirteenOrphans(snap.Counts);
        int pass = FindOption(actions, "Pass");
        bool keepHand = kokushiShanten <= 2 && pass >= 0;
        int option = keepHand ? pass : abort;

        if (EmjActuator.Call(option))
        {
            popupAttempts++;
            AmjsLog.Info(keepHand
                ? $"turn: nine terminals offered, keeping the hand (kokushi shanten {kokushiShanten}) -> [11, {option}] (attempt {popupAttempts})."
                : $"turn: nine terminals -> taking the redeal (kokushi shanten {kokushiShanten}) -> [11, {option}] (attempt {popupAttempts}).");
            actAtTick = Environment.TickCount64 + PopupRetryDelayMs; // verify next tick; re-fire if still up
        }

        return true;
    }

    private void DoCall()
    {
        if (popupAttempts >= MaxPopupAttempts)
        {
            AmjsLog.Warn("call: popup did not accept the fire after retries; yielding to the game timer.");
            acted = true;
            return;
        }

        var options = EmjBoardReader.ReadActionOptions();
        CallDecision decision = DecideCall(options);
        if (decision.Option < 0)
            return; // popup not readable yet, or nothing safe to click — wait for the next tick

        if (decision.Reason == "pass")
            decision = MaybeUpgradeToPon(options, decision);

        if (EmjActuator.Call(decision.Option))
        {
            popupAttempts++;
            AmjsLog.Info($"call: {decision.Reason} -> [11, {decision.Option}] (attempt {popupAttempts}).");
            if (popupAttempts == 1)
            {
                bool win = decision.Reason is "accept Ron" or "accept Tsumo";
                bool meld = decision.Reason.StartsWith("pon", StringComparison.Ordinal);
                Stats.Record(
                    win ? AutoActionKind.Ron : meld ? AutoActionKind.Meld : AutoActionKind.Pass,
                    win ? "Called the win" : meld ? decision.Reason : "Passed a call");
            }

            actAtTick = Environment.TickCount64 + PopupRetryDelayMs; // verify next tick; re-fire if still up
        }
    }

    /// <summary>
    /// When the safe default is to pass but the popup offers a pon, ask <see cref="CallPolicy"/>
    /// whether opening the hand on the tracked last discard is worth it. Everything must read
    /// cleanly (offered tile known, full 13-tile concealed hand) or the pass stands — a wrong pon
    /// is far worse than a missed one. Chi/kan stay declined: chi has an unverified
    /// variant-selection UI and kan flips dora the reader cannot see.
    /// </summary>
    private CallDecision MaybeUpgradeToPon(IReadOnlyList<ActionOption> options, CallDecision pass)
    {
        int pon = FindOption(options, "Pon");
        if (pon < 0 || lastDiscardTile < 0)
            return pass;

        if (!EmjBoardReader.TryReadHand(out HandSnapshot snap) || snap.TilesRead != 13 - 3 * snap.MeldCount)
            return pass;

        BoardSnapshot? board = EmjBoardReader.TryReadBoard();
        bool take = CallPolicy.ShouldPon(
            snap.Counts, snap.Melds, lastDiscardTile,
            BuildEvalContext(snap, board), board?.AnyOpponentRiichi ?? false);

        return take ? new CallDecision(pon, $"pon {Tiles.Name(lastDiscardTile)}") : pass;
    }

    /// <summary>
    /// Accept a win (Ron or Tsumo — the game only offers valid wins) if offered, otherwise pass.
    /// The pass may be upgraded to a pon afterwards (<see cref="MaybeUpgradeToPon"/>); chi and kan
    /// are never taken. The option to fire is resolved from the button labels, not a fixed index.
    /// Option -1 means "do nothing" — better to wait than to mis-click a call we didn't intend.
    /// </summary>
    internal static CallDecision DecideCall(IReadOnlyList<ActionOption> options)
    {
        if (options.Count == 0)
            return new CallDecision(-1, "no options readable");

        int ron = FindOption(options, "Ron");
        if (ron >= 0)
            return new CallDecision(ron, "accept Ron");

        int tsumo = FindOption(options, "Tsumo");
        if (tsumo >= 0)
            return new CallDecision(tsumo, "accept Tsumo");

        int pass = FindOption(options, "Pass");
        if (pass >= 0)
            return new CallDecision(pass, "pass");

        return new CallDecision(-1, $"no Ron/Tsumo/Pass among [{Labels(options)}]");
    }

    /// <summary>Non-destructively log the current popup's options and the choice the bot would make.</summary>
    public static void LogOptions()
    {
        var options = EmjBoardReader.ReadActionOptions();
        if (options.Count == 0)
        {
            AmjsLog.Info("options: no call/self-action buttons in the current popup.");
            return;
        }

        foreach (ActionOption o in options)
            AmjsLog.Info($"  [opt {o.Option}] '{o.Label}'");

        CallDecision d = DecideCall(options);
        AmjsLog.Info(d.Option >= 0
            ? $"options: bot would {d.Reason} -> [11, {d.Option}]."
            : $"options: bot would not act ({d.Reason}).");
    }

    private static int FindOption(IReadOnlyList<ActionOption> options, string substring)
    {
        foreach (ActionOption o in options)
            if (o.Label.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return o.Option;
        return -1;
    }

    private static string Labels(IReadOnlyList<ActionOption> options)
        => string.Join(", ", options.Select(o => $"'{o.Label}'"));

    internal readonly record struct CallDecision(int Option, string Reason);

    private int DelayFor(int state)
    {
        Configuration cfg = Plugin.Cfg;
        return state switch
        {
            6 or 28 or 30 => Roll(cfg.TurnDelayMinMs, cfg.TurnDelayMaxMs),   // human think time before discarding
            15 => Roll(cfg.CallDelayMinMs, cfg.CallDelayMaxMs),        // call-decision reaction
            29 or 32 => Roll(cfg.ResultDelayMinMs, cfg.ResultDelayMaxMs), // let the result screen settle
            _ => 0,
        };
    }

    private int Roll(int min, int max) => max <= min ? min : rng.Next(min, max);

    public void Dispose()
    {
        if (hooked)
            Plugin.Framework.Update -= OnUpdate;
    }
}
