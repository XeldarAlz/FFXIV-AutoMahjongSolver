using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using Mahjong.Plugin.Dalamud.Logging;
using Mahjong.Policy;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.Actions;

public sealed class AutoPlayLoop : IDisposable
{
    private const int ChiVariantSelectStateCode = 25;
    /// <summary>Agari / draw result modal between hands; carries Legal=None and a single "Next" button.</summary>
    private const int HandResultStateCode = 29;
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(3.0);
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(10.0);

    private const int VariantAcceptDelayMs = 500;
    private const int CallDecisionDelayMs = 700;
    private const int RiichiTsumogiriDelayMs = 700;
    private const int HandResultAdvanceDelayMs = 900;

    private const ActionFlags CallPromptFlags =
        ActionFlags.Pon | ActionFlags.Chi |
        ActionFlags.MinKan | ActionFlags.ShouMinKan |
        ActionFlags.Ron | ActionFlags.Riichi | ActionFlags.Tsumo;

    private readonly Plugin plugin;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly MahjongAddon addon;
    private readonly ActionStateMachine fsm = new(DispatchTimeout, RetryCooldown);
    private bool disposed;
    private string? lastSkipReason;

    public string LastActionDescription { get; private set; } = "(none)";

    public int LastObservedState { get; private set; } = -1;

    public int LastObservedHandCount { get; private set; } = -1;

    public AutoPlayLoop(Plugin plugin, IFramework framework, IPluginLog log, MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(addon);
        this.plugin = plugin;
        this.framework = framework;
        this.log = log;
        this.addon = addon;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        framework.Update -= OnUpdate;
    }

    private unsafe void OnUpdate(IFramework fw)
    {
        if (disposed)
            return;

        if (!IsAutomationArmed())
        {
            var cfg = plugin.Configuration;
            EmitSkipReason($"gate: tos={cfg.TosAccepted} armed={cfg.AutomationArmed} suggest_only={cfg.SuggestionOnly}",
                state: -1, hand: -1, flags: 0);
            return;
        }

        if (!ContinueAfterStuckRecovery())
        {
            EmitSkipReason("dispatch in flight (still within timeout)",
                state: -1, hand: -1, flags: 0);
            return;
        }

        // Runs before the snapshot guard: the result modal has no hand-array, so TryBuildSnapshot returns null and the post-snapshot state checks never fire.
        if (plugin.Configuration.AutoAdvanceAfterHand)
        {
            int earlyState = ReadStateCode();
            if (earlyState == HandResultStateCode)
            {
                LastObservedState = earlyState;
                LastObservedHandCount = -1;
                EmitProgressing();
                HandleHandResultAdvance(new DispatchContext(earlyState, -1));
                return;
            }
        }

        var snap = plugin.AddonReader.TryBuildSnapshot();

        // Runs before the snapshot-null guard so the windowExpired path still emits commit=false on transient nulls.
        CheckPendingDispatchOutcome(snap);

        CheckStuckStateAndEmit(snap);

        if (snap is null)
        {
            // Do not clear FSM context on transient snapshot misses — that would break the retry-cooldown debounce.
            EmitSkipReason("snapshot unavailable", state: -1, hand: -1, flags: 0);
            return;
        }

        int state = ReadStateCode();
        var context = new DispatchContext(state, snap.Hand.Count);
        LastObservedState = state;
        LastObservedHandCount = context.Hand;

        if (state == ChiVariantSelectStateCode)
        {
            EmitProgressing();
            HandleChiVariantSelect(context);
            return;
        }

        bool isCallPrompt = (snap.Legal.Flags & CallPromptFlags) != 0;
        bool isDiscardTurn = snap.Legal.Can(ActionFlags.Discard);
        int flags = (int)snap.Legal.Flags;

        // Riichi-confirm latch is hand-scoped via ObserveWall — popup signature drops mid-hand and clearing per-tick would let the loop redeclare riichi 20+ times in one hand.
        fsm.ObserveWall(snap.WallRemaining);

        if (!isCallPrompt && !isDiscardTurn)
        {
            // Do not clear FSM context on transient "not actionable" ticks — discard-animation gaps drop the Discard flag mid-commit and clearing here permits a duplicate dispatch.
            EmitSkipReason($"not actionable (state={state} hand={snap.Hand.Count} legal={snap.Legal.Flags})",
                state: state, hand: snap.Hand.Count, flags: flags);
            return;
        }

        if (fsm.ShouldSuppressForContext(context, DateTime.UtcNow))
        {
            EmitSkipReason($"suppressed for context (state={context.State} hand={context.Hand})",
                state: state, hand: snap.Hand.Count, flags: flags);
            return;
        }

        if (TryHandleRiichiConfirmTsumogiri(snap, context, isCallPrompt))
        {
            EmitProgressing();
            return;
        }

        EmitProgressing();
        if (isCallPrompt)
            ScheduleCallDecision(context);
        else
            ScheduleDiscard(context);
    }

    /// <summary>Dedup by exact reason string — loop ticks 60x/sec, so emit only on transitions.</summary>
    private void EmitSkipReason(string reason, int state, int hand, int flags)
    {
        if (lastSkipReason == reason)
            return;
        lastSkipReason = reason;
        log.Info($"[AutoPlayLoop] skip: {reason}");
        plugin.FindingsLog?.Record("hand_state_paused", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["state"] = state,
            ["hand"] = hand,
            ["flags"] = flags,
        });
    }

    private void EmitProgressing()
    {
        if (lastSkipReason is null)
            return;
        log.Info($"[AutoPlayLoop] resumed (was: {lastSkipReason})");
        lastSkipReason = null;
    }

    private void EmitDecisionFinding(string source, StateSnapshot snap, ActionChoice choice)
    {
        plugin.FindingsLog?.Record("decision", new Dictionary<string, object?>
        {
            ["source"] = source,
            ["kind"] = choice.Kind.ToString(),
            ["tile"] = choice.DiscardTile?.ToString(),
            ["hand_count"] = snap.Hand.Count,
            ["flags"] = (int)snap.Legal.Flags,
            ["pon_candidates"] = snap.Legal.PonCandidates.Count,
            ["chi_candidates"] = snap.Legal.ChiCandidates.Count,
            ["kan_candidates"] = snap.Legal.KanCandidates.Count,
            ["wall"] = snap.WallRemaining,
            ["reasoning"] = choice.Reasoning,
        });
    }

    private void EmitDispatchFinding(
        string label, InputDispatcher.DispatchResult result,
        int? option = null, Tile? tile = null, int? slot = null, int? state = null,
        StateSnapshot? snap = null)
    {
        plugin.FindingsLog?.Record("dispatch_attempted", new Dictionary<string, object?>
        {
            ["label"] = label,
            ["result"] = result.ToString(),
            ["option"] = option,
            ["tile"] = tile?.ToString(),
            ["slot"] = slot,
            ["state"] = state,
            ["path"] = plugin.Dispatcher.LastDiscardPath,
            ["cur_state"] = snap?.AddonStateCode,
            ["cur_hand"] = snap?.Hand.Count,
            ["cur_melds"] = snap?.OurMelds.Count,
            ["cur_legal"] = snap?.Legal.Flags.ToString(),
        });

        if (snap is not null)
        {
            pendingOutcome = new PendingDispatchOutcome(
                Label: label,
                DispatchedAt: DateTime.UtcNow,
                StateAtDispatch: snap.AddonStateCode,
                HandAtDispatch: snap.Hand.Count,
                MeldsAtDispatch: snap.OurMelds.Count,
                LastDispatchPath: plugin.Dispatcher.LastDiscardPath);
        }
    }

    private PendingDispatchOutcome? pendingOutcome;

    private static readonly TimeSpan DispatchOutcomeWindow = TimeSpan.FromMilliseconds(500);

    private readonly record struct PendingDispatchOutcome(
        string Label,
        DateTime DispatchedAt,
        int StateAtDispatch,
        int HandAtDispatch,
        int MeldsAtDispatch,
        string LastDispatchPath);

    private static readonly TimeSpan StuckStateThreshold = TimeSpan.FromSeconds(10);

    private int? stuckStateCode;
    private int? stuckHandCount;
    private ActionFlags? stuckLegal;
    private DateTime stuckSince;
    private bool stuckEmitted;

    private void CheckStuckStateAndEmit(StateSnapshot? snap)
    {
        if (snap is null)
            return;

        var legal = snap.Legal.Flags;
        if (stuckStateCode != snap.AddonStateCode
            || stuckHandCount != snap.Hand.Count
            || stuckLegal != legal)
        {
            stuckStateCode = snap.AddonStateCode;
            stuckHandCount = snap.Hand.Count;
            stuckLegal = legal;
            stuckSince = DateTime.UtcNow;
            stuckEmitted = false;
            return;
        }

        if (stuckEmitted)
            return;

        var elapsed = DateTime.UtcNow - stuckSince;
        if (elapsed < StuckStateThreshold)
            return;

        int[]? rawSlots = plugin.AddonReader.DumpHandArrayRaw();
        string handDump = rawSlots is null ? "(no raw)" : FormatHandArrayDump(rawSlots);
        int? activeTextureBase = plugin.AddonReader.ActiveLayout?.TileTextureBase;

        plugin.FindingsLog?.Record("stuck_state", new Dictionary<string, object?>
        {
            ["state"] = snap.AddonStateCode,
            ["hand"] = snap.Hand.Count,
            ["melds"] = snap.OurMelds.Count,
            ["legal"] = legal.ToString(),
            ["elapsed_ms"] = (int)elapsed.TotalMilliseconds,
            ["last_dispatch_path"] = plugin.Dispatcher.LastDiscardPath,
            ["last_action"] = LastActionDescription,
            ["hand_raw"] = rawSlots,
            ["tile_texture_base"] = activeTextureBase,
        });
        log.Warning(
            $"[AutoPlayLoop] STUCK at state={snap.AddonStateCode} hand={snap.Hand.Count} " +
            $"melds={snap.OurMelds.Count} legal={legal} for {(int)elapsed.TotalSeconds}s. " +
            $"Last dispatch: {LastActionDescription} (path={plugin.Dispatcher.LastDiscardPath}). " +
            $"Manual click required.");
        log.Warning($"[AutoPlayLoop] STUCK hand-array dump: {handDump}");
        stuckEmitted = true;
    }

    private string FormatHandArrayDump(int[] slots)
    {
        int textureBase = plugin.AddonReader.ActiveLayout?.TileTextureBase ?? 0;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < slots.Length; i++)
        {
            int raw = slots[i];
            string decoded;
            if (raw == 0)
                decoded = "  -";
            else
            {
                int idx = raw - textureBase;
                decoded = idx switch
                {
                    >= 0 and < 34 => Tile.FromId(idx).ToString(),
                    34 => "5m*",
                    35 => "5p*",
                    36 => "5s*",
                    _ => "??",
                };
            }
            sb.Append($"[{i:00}]={raw,5}({decoded,3})");
            if (i == 6)
                sb.Append(" | ");
            else if (i < slots.Length - 1)
                sb.Append(' ');
        }
        return sb.ToString();
    }

    private void CheckPendingDispatchOutcome(StateSnapshot? snap)
    {
        if (pendingOutcome is not { } pending)
            return;

        bool stateChanged = snap is not null &&
            (snap.AddonStateCode != pending.StateAtDispatch
             || snap.Hand.Count != pending.HandAtDispatch
             || snap.OurMelds.Count != pending.MeldsAtDispatch);
        bool windowExpired = DateTime.UtcNow - pending.DispatchedAt > DispatchOutcomeWindow;

        if (stateChanged)
        {
            plugin.FindingsLog?.Record("dispatch_outcome", new Dictionary<string, object?>
            {
                ["label"] = pending.Label,
                ["commit"] = true,
                ["path"] = pending.LastDispatchPath,
                ["state_at_dispatch"] = pending.StateAtDispatch,
                ["hand_at_dispatch"] = pending.HandAtDispatch,
                ["melds_at_dispatch"] = pending.MeldsAtDispatch,
                ["state_after"] = snap?.AddonStateCode,
                ["hand_after"] = snap?.Hand.Count,
                ["melds_after"] = snap?.OurMelds.Count,
                ["elapsed_ms"] = (int)(DateTime.UtcNow - pending.DispatchedAt).TotalMilliseconds,
            });
            pendingOutcome = null;
        }
        else if (windowExpired)
        {
            plugin.FindingsLog?.Record("dispatch_outcome", new Dictionary<string, object?>
            {
                ["label"] = pending.Label,
                ["commit"] = false,
                ["path"] = pending.LastDispatchPath,
                ["state_at_dispatch"] = pending.StateAtDispatch,
                ["hand_at_dispatch"] = pending.HandAtDispatch,
                ["melds_at_dispatch"] = pending.MeldsAtDispatch,
                ["state_after"] = snap?.AddonStateCode,
                ["hand_after"] = snap?.Hand.Count,
                ["melds_after"] = snap?.OurMelds.Count,
                ["elapsed_ms"] = (int)(DateTime.UtcNow - pending.DispatchedAt).TotalMilliseconds,
            });
            pendingOutcome = null;
        }
    }

    private bool IsAutomationArmed()
    {
        var cfg = plugin.Configuration;
        return cfg.TosAccepted && cfg.AutomationArmed && !cfg.SuggestionOnly;
    }

    private bool ContinueAfterStuckRecovery()
    {
        if (!fsm.IsDispatchInFlight)
            return true;
        if (fsm.TryRecoverFromStuckDispatch(DateTime.UtcNow))
        {
            log.Warning("[AutoPlayLoop] resetting stuck actionPending");
            return true;
        }
        return false;
    }

    private void HandleChiVariantSelect(DispatchContext context)
    {
        if (fsm.ShouldSuppressForContext(context, DateTime.UtcNow))
            return;
        ScheduleVariantAccept(context);
    }

    private void HandleHandResultAdvance(DispatchContext context)
    {
        if (fsm.ShouldSuppressForContext(context, DateTime.UtcNow))
            return;
        ScheduleHandResultAdvance(context);
    }

    /// <summary>Post-declaration Riichi: complete via tsumogiri instead of re-clicking the list (the list click no-ops at this point).</summary>
    private bool TryHandleRiichiConfirmTsumogiri(StateSnapshot snap, DispatchContext context, bool isCallPrompt)
    {
        if (!fsm.IsRiichiConfirmPending)
            return false;
        if (!isCallPrompt || !snap.Legal.Can(ActionFlags.Riichi))
            return false;
        if (context.Hand <= 0 || context.Hand % 3 != 2)
            return false;

        ScheduleRiichiTsumogiri(context);
        return true;
    }

    private void ScheduleAction(string label, DispatchContext context, int medianDelayMs, Action body)
    {
        fsm.BeginDispatch(DateTime.UtcNow, context);
        var delay = HumanTiming.RandomDelay(medianMs: medianDelayMs);
        _ = framework.RunOnTick(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                log.Error($"AutoPlayLoop {label} error: {ex}");
                LastActionDescription = $"{label} exception: {ex.Message}";
            }
            finally
            {
                fsm.CompleteDispatch();
            }
        }, delay);
    }

    private void ScheduleHandResultAdvance(DispatchContext context)
    {
        ScheduleAction("hand-result-next", context, HandResultAdvanceDelayMs, () =>
        {
            int currentState = ReadStateCode();
            if (currentState != HandResultStateCode)
            {
                LastActionDescription = $"hand-result-next aborted: state moved {HandResultStateCode}→{currentState}";
                return;
            }

            var snap = plugin.AddonReader.TryBuildSnapshot();
            var result = plugin.Dispatcher.DispatchHandResultNext();
            LastActionDescription = $"auto-hand-result-next → {result}";
            log.Info($"[AutoPlayLoop] hand-result-next dispatch: {LastActionDescription}");
            plugin.GameLogger.RecordAction(ActionKind.Pass, null, null, result.ToString(), "auto-advance after hand");
            EmitDispatchFinding("hand-result-next", result, state: currentState, snap: snap);
            ClearRetryDebounceIfHookFailed(result);
        });
    }

    private void ScheduleVariantAccept(DispatchContext context)
    {
        ScheduleAction("variant", context, VariantAcceptDelayMs, () =>
        {
            // Modal can close during the humanized delay — re-check at dispatch time.
            int currentState = ReadStateCode();
            if (currentState != ChiVariantSelectStateCode)
            {
                LastActionDescription = $"variant aborted: state moved {ChiVariantSelectStateCode}→{currentState}";
                return;
            }

            int bestIdx = 0;
            string scoreNote = "default(opt=0)";
            var variants = TryReadChiVariants();
            var snap = plugin.AddonReader.TryBuildSnapshot();
            if (variants is { Count: > 1 } && snap is not null)
                bestIdx = PickBestChiVariantIndex(variants, snap, out scoreNote);

            var result = plugin.Dispatcher.DispatchChiVariant(bestIdx);
            LastActionDescription = $"auto-variant[opt={bestIdx}] → {result} ({scoreNote})";
            log.Info($"[AutoPlayLoop] variant dispatch: {LastActionDescription}");
            plugin.GameLogger.RecordAction(ActionKind.Chi, null, bestIdx, result.ToString(), $"chi-variant: {scoreNote}");
            EmitDispatchFinding("chi-variant", result, option: bestIdx, state: currentState, snap: snap);
        });
    }

    /// <summary>Reads the chi-variant tile triples from AtkValues at the chi-variant-select popup. Capture 2026-05-25: atk[3]=variant_count, then 4 ints per variant (3 tile-IDs + 1 sentinel = textureBase).</summary>
    private unsafe IReadOnlyList<int[]>? TryReadChiVariants()
    {
        if (!addon.TryGet(out var unit, out _))
            return null;
        if (!unit->IsVisible || unit->AtkValues == null)
            return null;

        int atkCount = unit->AtkValuesCount;
        if (atkCount < 4)
            return null;

        var atk = unit->AtkValues;
        if (atk[3].Type != AtkValueType.Int)
            return null;
        int variantCount = atk[3].Int;
        if (variantCount is < 1 or > 8)
            return null;

        int textureBase = plugin.AddonReader.ActiveLayout?.TileTextureBase ?? 0;
        if (textureBase == 0)
            return null;

        int needed = 4 + variantCount * 4;
        if (atkCount < needed)
            return null;

        var variants = new List<int[]>(variantCount);
        for (int i = 0; i < variantCount; i++)
        {
            int baseIdx = 4 + i * 4;
            var tileIds = new int[3];
            for (int j = 0; j < 3; j++)
            {
                if (atk[baseIdx + j].Type != AtkValueType.Int)
                    return null;
                int id = HandArrayDecoder.DecodeTileId(atk[baseIdx + j].Int, textureBase, out _);
                if (id < 0)
                    return null;
                tileIds[j] = id;
            }
            variants.Add(tileIds);
        }
        return variants;
    }

    /// <summary>Picks the chi variant whose post-call closed hand has the lowest shanten. Tries all 3 (claim, hand-pair) splits per variant since the claimed tile isn't explicitly marked in AtkValues. Ties resolve to the lower variant index.</summary>
    private static int PickBestChiVariantIndex(IReadOnlyList<int[]> variants, StateSnapshot snap, out string note)
    {
        var counts = new int[Mahjong.Core.Tile.Count34];
        foreach (var t in snap.Hand)
            counts[t.Id]++;
        int meldsAfter = snap.OurMelds.Count + 1;

        int bestIdx = 0;
        int bestShanten = int.MaxValue;
        for (int v = 0; v < variants.Count; v++)
        {
            var tiles = variants[v];
            int? variantShanten = null;
            for (int claimSlot = 0; claimSlot < 3; claimSlot++)
            {
                int h1 = tiles[(claimSlot + 1) % 3];
                int h2 = tiles[(claimSlot + 2) % 3];
                if (counts[h1] < 1) continue;
                counts[h1]--;
                if (counts[h2] < 1) { counts[h1]++; continue; }
                counts[h2]--;
                int sh = Mahjong.Engine.ShantenCalculator.Standard(counts, meldsAfter);
                counts[h1]++;
                counts[h2]++;
                if (variantShanten is null || sh < variantShanten)
                    variantShanten = sh;
            }
            if (variantShanten is null) continue;
            if (variantShanten < bestShanten)
            {
                bestShanten = variantShanten.Value;
                bestIdx = v;
            }
        }

        note = bestShanten == int.MaxValue
            ? $"no formable variant, default(opt=0) of {variants.Count}"
            : $"shanten={bestShanten} across {variants.Count} variants";
        return bestIdx;
    }

    private void ScheduleDiscard(DispatchContext context)
    {
        ScheduleAction("discard", context, plugin.Configuration.HumanizedDelayMs, () =>
        {
            var snap = plugin.AddonReader.TryBuildSnapshot();
            int currentState = ReadStateCode();
            if (snap is null || !snap.Legal.Can(ActionFlags.Discard))
            {
                LastActionDescription = $"discard aborted: not a discard state (state={currentState} hand={snap?.Hand.Count ?? -1} flags={snap?.Legal.Flags.ToString() ?? "null"})";
                log.Info($"[AutoPlayLoop] {LastActionDescription}");
                return;
            }

            var choice = plugin.Policy.Choose(snap);
            log.Info(
                $"[AutoPlayLoop] discard body: schedState={context.State} curState={currentState} " +
                $"hand={snap.Hand.Count} melds={snap.OurMelds.Count} flags={snap.Legal.Flags} " +
                $"choice={choice.Kind} tile={choice.DiscardTile}");
            EmitDecisionFinding("discard", snap, choice);
            DispatchPolicyChoice(snap, choice);
            log.Info(
                $"[AutoPlayLoop] discard body done: {LastActionDescription} " +
                $"path={plugin.Dispatcher.LastDiscardPath}");
        });
    }

    /// <summary>On HookFailed clear the FSM context — otherwise the 3 s retry debounce keeps the bot stranded on a missed click.</summary>
    private void ClearRetryDebounceIfHookFailed(InputDispatcher.DispatchResult result)
    {
        if (result == InputDispatcher.DispatchResult.HookFailed)
            fsm.ClearContext();
    }

    private void ScheduleCallDecision(DispatchContext context)
    {
        ScheduleAction("call", context, CallDecisionDelayMs, () =>
        {
            var snap = plugin.AddonReader.TryBuildSnapshot();
            int currentState = ReadStateCode();
            if (snap is null)
            {
                LastActionDescription = $"call: no snapshot (state={currentState})";
                log.Info($"[AutoPlayLoop] {LastActionDescription}");
                return;
            }
            var choice = plugin.Policy.Choose(snap);
            log.Info(
                $"[AutoPlayLoop] call body: schedState={context.State} curState={currentState} " +
                $"hand={snap.Hand.Count} melds={snap.OurMelds.Count} flags={snap.Legal.Flags} " +
                $"choice={choice.Kind} tile={choice.DiscardTile}");
            EmitDecisionFinding("call", snap, choice);
            DispatchCallChoice(snap, choice);
            // LastDiscardPath belongs to DispatchDiscard — do not print it on the call path; it would be stale.
            log.Info(
                $"[AutoPlayLoop] call body done: {LastActionDescription} " +
                $"pon={snap.Legal.PonCandidates.Count} chi={snap.Legal.ChiCandidates.Count} " +
                $"kan={snap.Legal.KanCandidates.Count}");
        });
    }

    private void ScheduleRiichiTsumogiri(DispatchContext context)
    {
        ScheduleAction("riichi-tsumogiri", context, RiichiTsumogiriDelayMs, () =>
        {
            var snap = plugin.AddonReader.TryBuildSnapshot();
            if (snap is null || snap.Hand.Count < 14)
            {
                LastActionDescription = $"riichi-tsumogiri aborted: hand={snap?.Hand.Count ?? -1}";
                return;
            }

            // Latch carries the policy-chosen tile; fall back to slot 13 only when no tile was latched (post-confirm yaku-preview popup).
            int slot;
            Tile tile;
            if (fsm.RiichiConfirmTile is { } target)
            {
                slot = plugin.AddonReader.FindAddonSlotOfTile(target);
                if (slot < 0)
                {
                    LastActionDescription = $"riichi-tsumogiri aborted: latched tile {target} not in hand";
                    log.Info($"[AutoPlayLoop] {LastActionDescription}");
                    return;
                }
                tile = target;
            }
            else
            {
                slot = 13;
                tile = snap.Hand[13];
            }

            var result = plugin.Dispatcher.DispatchDiscard(slot);
            LastActionDescription = $"auto-riichi-tsumogiri {tile} slot={slot} → {result}";
            log.Info($"[AutoPlayLoop] riichi-tsumogiri dispatch: {LastActionDescription}");
            plugin.GameLogger.RecordAction(ActionKind.Discard, tile, slot, result.ToString(), "riichi-tsumogiri");
            EmitDispatchFinding("riichi-tsumogiri", result, tile: tile, slot: slot, snap: snap);
            ClearRetryDebounceIfHookFailed(result);
            // Do not clear the latch here — ObserveWall clears it on the next hand; otherwise policy.Choose would re-approve riichi in the same hand.
        });
    }

    private void DispatchPolicyChoice(StateSnapshot snap, ActionChoice choice)
    {
        if (choice.Kind == ActionKind.AnKan && choice.DiscardTile is { } kanTile)
        {
            DispatchAnkan(snap, choice, kanTile);
            return;
        }

        if (choice.Kind == ActionKind.Pass && IsHandOutOfSyncReason(choice.Reasoning)
            && snap.Legal.Can(ActionFlags.Discard))
        {
            DispatchOutOfSyncTsumogiri(snap, choice);
            return;
        }

        if (choice.Kind != ActionKind.Discard && choice.Kind != ActionKind.Riichi)
        {
            LastActionDescription = $"policy returned {choice.Kind} — not dispatching";
            return;
        }
        if (choice.DiscardTile is null)
        {
            LastActionDescription = $"policy {choice.Kind} missing tile";
            return;
        }

        DispatchDiscardOrRiichi(snap, choice);
    }

    /// <summary>
    /// Matches <c>EfficiencyPolicy.TsumogiriFallback</c>'s pause reason. When the meld tracker
    /// has fallen behind (typically post-ShouMinKan or a missed pon/chi inference race), the
    /// policy refuses to score a hand whose closed+meld arithmetic ≠ 14. Without a recovery
    /// path the autoplay loop debounces forever on the same Pass and the bot softlocks.
    /// </summary>
    private static bool IsHandOutOfSyncReason(string? reasoning) =>
        reasoning is not null
        && reasoning.StartsWith("hand state out of sync", StringComparison.Ordinal);

    /// <summary>
    /// Blind tsumogiri at slot 13 — the addon parks the just-drawn tile there even under the
    /// post-call sparse layout (HandArrayDecoder.cs:7). Safer than guessing a real discard
    /// off an incomplete view, and keeps the hand moving instead of softlocking.
    /// </summary>
    private void DispatchOutOfSyncTsumogiri(StateSnapshot snap, ActionChoice passChoice)
    {
        if (snap.Hand.Count == 0)
        {
            LastActionDescription = "oos-tsumogiri aborted: empty hand";
            log.Warning($"[AutoPlayLoop] {LastActionDescription}");
            return;
        }

        // Hand[^1] mirrors the slot-13-preferred decode order in HandArrayDecoder.ReadHand
        // (scans 0..len-1, so the highest occupied slot ends up last). FindAddonSlotOfTile
        // resolves it back to slot 13 when the tile sits there, otherwise to its actual slot.
        Tile drawn = snap.Hand[^1];
        int slot = plugin.AddonReader.FindAddonSlotOfTile(drawn);
        if (slot < 0)
        {
            LastActionDescription = $"oos-tsumogiri aborted: drawn tile {drawn} not in addon hand";
            log.Warning($"[AutoPlayLoop] {LastActionDescription}");
            return;
        }

        var result = plugin.Dispatcher.DispatchDiscard(slot);
        LastActionDescription = $"oos-tsumogiri recovery {drawn} slot={slot} → {result}";
        log.Warning(
            $"[AutoPlayLoop] {LastActionDescription} " +
            $"(policy reason: {passChoice.Reasoning})");
        plugin.GameLogger.RecordAction(
            ActionKind.Discard, drawn, slot, result.ToString(),
            $"oos-tsumogiri: {passChoice.Reasoning}");
        EmitDispatchFinding("oos-tsumogiri", result, tile: drawn, slot: slot, snap: snap);
        ClearRetryDebounceIfHookFailed(result);
    }

    private void DispatchAnkan(StateSnapshot snap, ActionChoice choice, Tile kanTile)
    {
        // Route AnKan through opcode 11 (call-prompt button-row) — the speculative opcode-12 path was a no-op in the addon.
        int acceptIndex = ComputeAcceptIndex(ActionKind.AnKan, snap.Legal, null);
        var result = plugin.Dispatcher.DispatchCallOption(acceptIndex);
        LastActionDescription = $"auto-ankan {kanTile} opt={acceptIndex} → {result}";
        plugin.GameLogger.RecordAction(ActionKind.AnKan, kanTile, acceptIndex, result.ToString(), choice.Reasoning);
        EmitDispatchFinding("ankan", result, option: acceptIndex, tile: kanTile, snap: snap);
        ClearRetryDebounceIfHookFailed(result);

        // Self-declared kans produce no opp-discard signal; MeldTracker.ObserveSnapshot cannot infer them, so record here to preserve the 14-tile invariant.
        if (result == InputDispatcher.DispatchResult.Ok)
            plugin.MeldTracker.Record(Meld.AnKan(kanTile));
    }

    private void DispatchDiscardOrRiichi(StateSnapshot snap, ActionChoice choice)
    {
        var tile = choice.DiscardTile!.Value;
        int slot = plugin.AddonReader.FindAddonSlotOfTile(tile);
        if (slot < 0)
        {
            LastActionDescription = $"tile {tile} not in hand";
            return;
        }

        // Riichi at state-6: click via opcode-11 and latch the policy's chosen tile so the next-tick tsumogiri commits the ukeire-max tile, not slot 13.
        if (choice.Kind == ActionKind.Riichi && snap.Legal.Can(ActionFlags.Riichi))
        {
            int riichiIdx = ComputeAcceptIndex(ActionKind.Riichi, snap.Legal, null);
            var rResult = plugin.Dispatcher.DispatchCallOption(riichiIdx);
            LastActionDescription = $"auto-riichi[opt={riichiIdx}] (tile={tile}) → {rResult}";
            plugin.GameLogger.RecordAction(ActionKind.Riichi, tile, riichiIdx, rResult.ToString(), choice.Reasoning);
            EmitDispatchFinding("riichi", rResult, option: riichiIdx, tile: tile, snap: snap);
            fsm.LatchRiichiConfirm(tile);
            ClearRetryDebounceIfHookFailed(rResult);
            return;
        }

        var result = plugin.Dispatcher.DispatchDiscard(slot);
        LastActionDescription = $"auto-discard {tile} slot={slot} → {result}";
        plugin.GameLogger.RecordAction(ActionKind.Discard, tile, slot, result.ToString(), choice.Reasoning);
        EmitDispatchFinding("discard", result, tile: tile, slot: slot, snap: snap);
        ClearRetryDebounceIfHookFailed(result);
    }

    private void DispatchCallChoice(StateSnapshot snap, ActionChoice choice)
    {
        var legal = snap.Legal;

        // State-6 popup is dual-use: it offers Riichi/Tsumo/AnKan and lists discardable tiles — route Discard/Riichi through the list-widget path, not Pass.
        if (choice.Kind is ActionKind.Discard or ActionKind.Riichi
            && choice.DiscardTile.HasValue)
        {
            DispatchPolicyChoice(snap, choice);
            log.Info($"[AutoPlayLoop] discard-from-call-popup dispatch: {LastActionDescription}");
            return;
        }

        bool acceptRiichiPopup = ResolveRiichiPopupAcceptance(snap, choice, out var riichiProbeTile, out var riichiReason);

        bool shouldAccept = acceptRiichiPopup || choice.Kind is
            ActionKind.Ron or ActionKind.Tsumo or
            ActionKind.Pon or ActionKind.Chi or
            ActionKind.AnKan or ActionKind.MinKan or ActionKind.ShouMinKan;

        if (shouldAccept)
            DispatchAccept(snap, choice, legal, acceptRiichiPopup, riichiProbeTile, riichiReason!);
        else
            DispatchPass(snap, choice, legal, riichiReason);

        log.Info($"[AutoPlayLoop] call-prompt dispatch: {LastActionDescription}");
    }

    /// <summary>For an initial Riichi popup, re-run policy against a synthetic Discard|Riichi snapshot so RiichiPolicy actually fires — the standard call branch skips Riichi.</summary>
    private bool ResolveRiichiPopupAcceptance(StateSnapshot snap, ActionChoice choice, out Tile? probeTile, out string? probeReason)
    {
        probeReason = null;
        probeTile = null;
        if (choice.Kind != ActionKind.Pass || !snap.Legal.Can(ActionFlags.Riichi))
            return false;

        if (fsm.IsRiichiConfirmPending || snap.Hand.Count == 0 || snap.Hand.Count % 3 != 2)
        {
            probeReason = "riichi-confirm";
            return true;
        }

        var probe = snap with
        {
            Legal = snap.Legal with
            {
                Flags = ActionFlags.Discard | ActionFlags.Riichi,
            },
        };
        var verdict = plugin.Policy.Choose(probe);
        if (verdict.Kind != ActionKind.Riichi)
        {
            probeReason = string.IsNullOrEmpty(verdict.Reasoning)
                ? "riichi declined by policy"
                : $"riichi declined: {verdict.Reasoning}";
            return false;
        }

        probeTile = verdict.DiscardTile;
        probeReason = string.IsNullOrEmpty(verdict.Reasoning) ? "riichi-accept" : verdict.Reasoning;
        return true;
    }

    private void DispatchAccept(StateSnapshot snap, ActionChoice choice, LegalActions legal, bool acceptRiichiPopup, Tile? riichiProbeTile, string riichiReason)
    {
        // Every accept flows through opcode 11 / SelectItem (DispatchCallOption auto-routes by popup shape). The dedicated Tsumo opcode-9 path no-opped at state-6 SelfDeclareList because that popup is a list widget — the corpus capture of opcode 9 was the addon's internal callback fired *after* SelectItem ran, not a click-equivalent payload.
        var loggedKind = acceptRiichiPopup ? ActionKind.Riichi : choice.Kind;
        int acceptIndex = acceptRiichiPopup
            ? ComputeAcceptIndex(ActionKind.Riichi, legal, choice.Call)
            : ComputeAcceptIndex(choice.Kind, legal, choice.Call);
        var result2 = plugin.Dispatcher.DispatchCallOption(acceptIndex);
        string label = acceptRiichiPopup ? "riichi-confirm" : choice.Kind.ToString().ToLowerInvariant();
        LastActionDescription = $"auto-{label}[opt={acceptIndex}] → {result2}";
        plugin.GameLogger.RecordAction(
            loggedKind, null, acceptIndex, result2.ToString(),
            acceptRiichiPopup ? riichiReason : choice.Reasoning);
        EmitDispatchFinding(label, result2, option: acceptIndex, snap: snap);
        ClearRetryDebounceIfHookFailed(result2);

        // Yaku-preview confirm popup shares the Riichi-flag signature — latch to prevent retry-dispatch and carry the probe's chosen discard.
        if (acceptRiichiPopup)
            fsm.LatchRiichiConfirm(riichiProbeTile);

        // ShouMinKan: addon shrinks the closed hand by 1 and the existing pon ought to grow to a kan,
        // but ObserveSnapshot can't infer that from a delta=1. Upgrade the meld in-place so meld-tile
        // arithmetic stays at 14 and the policy doesn't fall into out-of-sync Pass.
        if (result2 == InputDispatcher.DispatchResult.Ok
            && choice.Kind == ActionKind.ShouMinKan
            && choice.Call is { } shouCand)
        {
            plugin.MeldTracker.UpgradeToShouMinKan(shouCand.ClaimedTile);
        }
    }

    private void DispatchPass(StateSnapshot snap, ActionChoice choice, LegalActions legal, string? reasonOverride = null)
    {
        // Pass index = count of accept buttons (multi-chi adds one slot per chi candidate).
        int passIndex = ComputePassIndex(legal);
        var result = plugin.Dispatcher.DispatchCallOption(passIndex);
        LastActionDescription = $"auto-pass[opt={passIndex}] → {result}";
        string reasoning = string.IsNullOrEmpty(reasonOverride) ? choice.Reasoning : reasonOverride;
        plugin.GameLogger.RecordAction(ActionKind.Pass, null, passIndex, result.ToString(), reasoning);
        EmitDispatchFinding("pass", result, option: passIndex, snap: snap);
    }

    /// <summary>Call-row button order Pon, Chi, AnKan, MinKan, ShouMinKan, Ron, Riichi, Tsumo, Pass; Chi is one slot regardless of ChiCandidates.Count (variant picked in state-25 sub-popup).</summary>
    internal static int ComputeAcceptIndex(ActionKind kind, LegalActions legal, MeldCandidate? chosenCall)
    {
        int idx = 0;

        if (kind == ActionKind.Pon)
            return idx;
        if (legal.Can(ActionFlags.Pon))
            idx++;

        if (kind == ActionKind.Chi)
            return idx;
        if (legal.Can(ActionFlags.Chi))
            idx++;

        if (kind == ActionKind.AnKan)
            return idx;
        if (legal.Can(ActionFlags.AnKan))
            idx++;

        if (kind == ActionKind.MinKan)
            return idx;
        if (legal.Can(ActionFlags.MinKan))
            idx++;

        if (kind == ActionKind.ShouMinKan)
            return idx;
        if (legal.Can(ActionFlags.ShouMinKan))
            idx++;

        if (kind == ActionKind.Ron)
            return idx;
        if (legal.Can(ActionFlags.Ron))
            idx++;

        if (kind == ActionKind.Riichi)
            return idx;
        if (legal.Can(ActionFlags.Riichi))
            idx++;

        if (kind == ActionKind.Tsumo)
            return idx;

        return 0;
    }

    /// <summary>Index of the Pass button on a call-prompt row: one slot per offered accept action (see <see cref="ComputeAcceptIndex"/>), Pass closes the row.</summary>
    internal static int ComputePassIndex(LegalActions legal)
    {
        int idx = 0;
        if (legal.Can(ActionFlags.Pon))
            idx++;
        if (legal.Can(ActionFlags.Chi))
            idx++;
        if (legal.Can(ActionFlags.AnKan))
            idx++;
        if (legal.Can(ActionFlags.MinKan))
            idx++;
        if (legal.Can(ActionFlags.ShouMinKan))
            idx++;
        if (legal.Can(ActionFlags.Ron))
            idx++;
        if (legal.Can(ActionFlags.Riichi))
            idx++;
        if (legal.Can(ActionFlags.Tsumo))
            idx++;
        return idx;
    }

    private unsafe int ReadStateCode()
    {
        if (!addon.TryGet(out var unit, out _))
            return -1;
        if (!unit->IsVisible || unit->AtkValues == null || unit->AtkValuesCount == 0)
            return -1;
        var v = unit->AtkValues[0];
        return v.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v.Int : -1;
    }
}
