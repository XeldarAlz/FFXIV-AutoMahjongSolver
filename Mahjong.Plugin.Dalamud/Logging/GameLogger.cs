using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Game;
using Mahjong.Policy;

namespace Mahjong.Plugin.Dalamud.Logging;

public sealed class GameLogger : IDisposable
{
    public const int SchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly StateAggregator? aggregator;
    private readonly IConfigService<Configuration> configService;
    private readonly IPluginLog log;
    private readonly string gamesDir;
    private readonly object writerLock = new();
    private readonly Func<IPolicy>? policyAccessor;
    private readonly Func<MeldTrackerStateDto>? meldTrackerAccessor;
    private readonly InputEventLogger? eventLogger;

    private string? currentPath;
    private int handSeq;
    private int lastWall = -1;
    private int? lastStateHash;
    private int[]? lastHandStartScores;
    private bool disposed;

    public string? CurrentPath => currentPath;
    public int HandSeq => handSeq;
    public string GamesDir => gamesDir;

    public GameLogger(
        StateAggregator aggregator,
        IConfigService<Configuration> configService,
        IPluginLog log,
        string pluginConfigDir,
        Func<IPolicy>? policyAccessor = null,
        InputEventLogger? eventLogger = null,
        Func<MeldTrackerStateDto>? meldTrackerAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDir);
        this.aggregator = aggregator;
        this.configService = configService;
        this.log = log;
        this.policyAccessor = policyAccessor;
        this.eventLogger = eventLogger;
        this.meldTrackerAccessor = meldTrackerAccessor;
        gamesDir = Path.Combine(pluginConfigDir, "games");
        Directory.CreateDirectory(gamesDir);
        aggregator.Changed += OnStateChanged;
        if (eventLogger is not null)
            eventLogger.CallPromptObserved += OnCallPromptObserved;
    }

    /// <summary>Test-only: skips aggregator wiring.</summary>
    internal GameLogger(
        IConfigService<Configuration> configService,
        IPluginLog log,
        string pluginConfigDir)
    {
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDir);
        aggregator = null;
        this.configService = configService;
        this.log = log;
        policyAccessor = null;
        eventLogger = null;
        gamesDir = Path.Combine(pluginConfigDir, "games");
        Directory.CreateDirectory(gamesDir);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (aggregator is not null)
            aggregator.Changed -= OnStateChanged;
        if (eventLogger is not null)
            eventLogger.CallPromptObserved -= OnCallPromptObserved;
    }

    internal void OnStateChanged(StateSnapshot snap)
    {
        if (!configService.Current.EnableGameLogging)
            return;

        // StateAggregator.Changed fires per-frame; dedup by structural hash or one turn yields ~1200 duplicate lines.
        int hash = ComputeContentHash(snap);
        if (lastStateHash == hash)
            return;
        lastStateHash = hash;

        try
        {
            MaybeRollHand(snap);
            WriteLine(JsonSerializer.Serialize(BuildStateEvent(snap), JsonOpts));
            MaybeRecordDecision(snap);
        }
        catch (Exception ex)
        {
            log.Error($"GameLogger state-write error: {ex.Message}");
        }
    }

    private void MaybeRecordDecision(StateSnapshot snap)
    {
        if (policyAccessor is null)
            return;
        if (snap.Legal.Flags == ActionFlags.None)
            return;
        ActionChoice choice;
        try
        { choice = policyAccessor().Choose(snap); }
        catch (Exception ex)
        {
            log.Error($"GameLogger decision-eval error: {ex.Message}");
            return;
        }
        try
        {
            var tracker = meldTrackerAccessor?.Invoke();
            WriteLine(JsonSerializer.Serialize(BuildDecisionEvent(choice, tracker), JsonOpts));
        }
        catch (Exception ex)
        {
            log.Error($"GameLogger decision-write error: {ex.Message}");
        }
    }

    private void OnCallPromptObserved(CallPromptEvent evt)
    {
        if (disposed || !configService.Current.EnableGameLogging)
            return;
        if (currentPath is null)
            return;
        try
        {
            var dto = new CallPromptDto(
                T: evt.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                E: "call-prompt",
                Variant: evt.AddonName,
                StateCode: evt.StateCode,
                Flags: evt.Flags,
                Pon: evt.PonClaimedTileIds,
                Chi: evt.ChiClaimedTileIds,
                Kan: evt.KanClaimedTileIds,
                Av: evt.IntValues);
            WriteLine(JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch (Exception ex)
        {
            log.Error($"GameLogger call-prompt-write error: {ex.Message}");
        }
    }

    public void RecordAction(ActionKind kind, Tile? tile, int? slot, string result, string reasoning)
    {
        if (!configService.Current.EnableGameLogging || disposed)
            return;
        try
        {
            var evt = new ActionEvent(
                T: Now(),
                E: "action",
                Kind: kind.ToString(),
                Tile: tile?.Id,
                Slot: slot,
                Result: result,
                Why: string.IsNullOrEmpty(reasoning) ? null : reasoning);
            WriteLine(JsonSerializer.Serialize(evt, JsonOpts));
        }
        catch (Exception ex)
        {
            log.Error($"GameLogger action-write error: {ex.Message}");
        }
    }

    /// <summary>Roll only on wall-jump-up AND hand at deal-shape count (0/13/14); mid-hand jumps are read glitches.</summary>
    private void MaybeRollHand(StateSnapshot snap)
    {
        bool firstRoll = currentPath is null;
        bool wallJumpUp = !firstRoll && snap.WallRemaining > lastWall + 5;
        if (!firstRoll && !wallJumpUp)
        {
            lastWall = snap.WallRemaining;
            return;
        }
        // Wall jumped but hand isn't deal-shape — retain lastWall so the next tick re-attempts the roll.
        if (wallJumpUp && snap.Hand.Count != 0 && snap.Hand.Count != 13 && snap.Hand.Count != 14)
            return;
        lastWall = snap.WallRemaining;

        // Write hand-end into the NEW file so TelemetryUploader can't move the old file between writes.
        var previousStartScores = lastHandStartScores;
        bool emitHandEnd = !firstRoll && previousStartScores is not null;

        RollWriter();

        if (emitHandEnd)
            EmitHandEnd(previousStartScores!, snap.Scores);

        var startScores = snap.Scores.ToArray();
        lastHandStartScores = startScores;
        var start = new HandStartEvent(
            T: Now(),
            E: "hand-start",
            V: SchemaVersion,
            Seat: snap.OurSeat,
            RoundWind: snap.RoundWind,
            Dealer: snap.DealerSeat,
            Honba: snap.Honba,
            RiichiSticks: snap.RiichiSticks,
            Scores: startScores);
        WriteLine(JsonSerializer.Serialize(start, JsonOpts));
    }

    private void EmitHandEnd(IReadOnlyList<int> scoresBefore, IReadOnlyList<int> scoresAfter)
    {
        int n = Math.Min(scoresBefore.Count, scoresAfter.Count);
        var deltas = new int[n];
        for (int i = 0; i < n; i++)
            deltas[i] = scoresAfter[i] - scoresBefore[i];
        var (kind, winner, loser) = InferResultKind(deltas);
        var evt = new HandEndEvent(
            T: Now(),
            E: "hand-end",
            Kind: kind,
            Winner: winner,
            Loser: loser,
            Deltas: deltas,
            ScoresAfter: scoresAfter.ToArray());
        try
        { WriteLine(JsonSerializer.Serialize(evt, JsonOpts)); }
        catch (Exception ex) { log.Error($"GameLogger hand-end-write error: {ex.Message}"); }
    }

    internal static (string kind, int? winner, int? loser) InferResultKind(int[] deltas)
    {
        int pos = 0, neg = 0;
        int winnerIdx = -1, loserIdx = -1;
        int maxPos = 0, minNeg = 0;
        for (int i = 0; i < deltas.Length; i++)
        {
            if (deltas[i] > 0)
            {
                pos++;
                if (deltas[i] > maxPos)
                { maxPos = deltas[i]; winnerIdx = i; }
            }
            else if (deltas[i] < 0)
            {
                neg++;
                if (deltas[i] < minNeg)
                { minNeg = deltas[i]; loserIdx = i; }
            }
        }
        if (pos == 1 && neg == 1)
            return ("ron", winnerIdx, loserIdx);
        if (pos == 1 && neg == 3)
            return ("tsumo", winnerIdx, null);
        return ("draw", null, null);
    }

    private void RollWriter()
    {
        lock (writerLock)
        {
            handSeq++;
            var fn = $"game-{DateTime.UtcNow:yyyyMMdd-HHmmss}-hand{handSeq:D2}.ndjson";
            currentPath = Path.Combine(gamesDir, fn);
        }
    }

    /// <summary>Open-write-close per line — a persistent StreamWriter blocks TelemetryUploader's FileShare.Read.</summary>
    private void WriteLine(string line)
    {
        if (currentPath is null)
            return;
        lock (writerLock)
        {
            try
            {
                using var w = new StreamWriter(new FileStream(
                    currentPath, FileMode.Append, FileAccess.Write, FileShare.Read));
                w.WriteLine(line);
            }
            catch (Exception ex)
            {
                log.Error($"GameLogger write error: {ex.Message}");
            }
        }
    }

    private static int ComputeContentHash(StateSnapshot snap)
    {
        var h = new HashCode();
        h.Add(snap.WallRemaining);
        h.Add(snap.TurnIndex);
        h.Add((int)snap.Legal.Flags);
        h.Add(snap.OurRiichi);
        h.Add(snap.OurIppatsu);
        h.Add(snap.OurSeat);
        h.Add(snap.RoundWind);
        h.Add(snap.DealerSeat);
        h.Add(snap.Honba);
        h.Add(snap.RiichiSticks);
        foreach (var t in snap.Hand)
            h.Add(t.Id);
        foreach (var m in snap.OurMelds)
        {
            h.Add((int)m.Kind);
            foreach (var t in m.Tiles)
                h.Add(t.Id);
        }
        foreach (var t in snap.DoraIndicators)
            h.Add(t.Id);
        foreach (var s in snap.Scores)
            h.Add(s);
        foreach (var s in snap.Seats)
        {
            h.Add(s.DiscardCount);
            foreach (var t in s.Discards)
                h.Add(t.Id);
            foreach (var m in s.Melds)
            {
                h.Add((int)m.Kind);
                foreach (var t in m.Tiles)
                    h.Add(t.Id);
            }
            h.Add(s.Riichi);
            h.Add(s.RiichiDiscardIndex);
            h.Add(s.Ippatsu);
        }
        return h.ToHashCode();
    }

    private static DecisionEvent BuildDecisionEvent(ActionChoice choice, MeldTrackerStateDto? tracker) => new(
        T: Now(),
        E: "decision",
        Kind: choice.Kind.ToString(),
        Tile: choice.DiscardTile?.Id,
        CallKind: choice.Call?.Kind.ToString(),
        Why: string.IsNullOrEmpty(choice.Reasoning) ? null : choice.Reasoning,
        Steps: choice.Steps is { Count: > 0 } steps
            ? steps.Select(r => new StepDto(K: r.Code, D: r.Display)).ToArray()
            : null,
        Tracker: tracker is { } t
            ? new TrackerDto(
                Melds: t.Melds,
                DeferredTicks: t.DeferredTicks,
                PendingSeat: t.PendingOppDiscardSeat,
                MeldAkadora: t.MeldAkadora)
            : null);

    private static StateEvent BuildStateEvent(StateSnapshot snap) => new(
        T: Now(),
        E: "state",
        StateCode: snap.AddonStateCode,
        Wall: snap.WallRemaining,
        Turn: snap.TurnIndex,
        Hand: snap.Hand.Select(t => (int)t.Id).ToArray(),
        OurMelds: snap.OurMelds.Select(ToMeldDto).ToArray(),
        Dora: snap.DoraIndicators.Select(t => (int)t.Id).ToArray(),
        OurRiichi: snap.OurRiichi,
        OurIppatsu: snap.OurIppatsu,
        Legal: snap.Legal.Flags.ToString(),
        Scores: snap.Scores.ToArray(),
        Seats: snap.Seats.Select(ToSeatDto).ToArray());

    private static SeatDto ToSeatDto(SeatView s) => new(
        Dc: s.DiscardCount,
        D: s.Discards.Select(t => (int)t.Id).ToArray(),
        M: s.Melds.Select(ToMeldDto).ToArray(),
        R: s.Riichi,
        Ri: s.RiichiDiscardIndex,
        Ip: s.Ippatsu);

    private static MeldDto ToMeldDto(Meld m) => new(
        K: m.Kind.ToString(),
        T: m.Tiles.Select(t => (int)t.Id).ToArray(),
        C: m.ClaimedTile?.Id,
        Fs: m.ClaimedFromSeat);

    private static string Now() =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private sealed record HandStartEvent(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("seat")] int Seat,
        [property: JsonPropertyName("round_wind")] int RoundWind,
        [property: JsonPropertyName("dealer")] int Dealer,
        [property: JsonPropertyName("honba")] int Honba,
        [property: JsonPropertyName("riichi_sticks")] int RiichiSticks,
        [property: JsonPropertyName("scores")] int[] Scores);

    private sealed record HandEndEvent(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("winner")] int? Winner,
        [property: JsonPropertyName("loser")] int? Loser,
        [property: JsonPropertyName("deltas")] int[] Deltas,
        [property: JsonPropertyName("scores_after")] int[] ScoresAfter);

    private sealed record StateEvent(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("state_code")] int StateCode,
        [property: JsonPropertyName("wall")] int Wall,
        [property: JsonPropertyName("turn")] int Turn,
        [property: JsonPropertyName("hand")] int[] Hand,
        [property: JsonPropertyName("our_melds")] MeldDto[] OurMelds,
        [property: JsonPropertyName("dora")] int[] Dora,
        [property: JsonPropertyName("our_riichi")] bool OurRiichi,
        [property: JsonPropertyName("our_ippatsu")] bool OurIppatsu,
        [property: JsonPropertyName("legal")] string Legal,
        [property: JsonPropertyName("scores")] int[] Scores,
        [property: JsonPropertyName("seats")] SeatDto[] Seats);

    private sealed record ActionEvent(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("tile")] int? Tile,
        [property: JsonPropertyName("slot")] int? Slot,
        [property: JsonPropertyName("result")] string Result,
        [property: JsonPropertyName("why")] string? Why);

    private sealed record DecisionEvent(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("tile")] int? Tile,
        [property: JsonPropertyName("call_kind")] string? CallKind,
        [property: JsonPropertyName("why")] string? Why,
        [property: JsonPropertyName("steps")] StepDto[]? Steps,
        [property: JsonPropertyName("tracker")] TrackerDto? Tracker);

    private sealed record StepDto(
        [property: JsonPropertyName("k")] string K,
        [property: JsonPropertyName("d")] string D);

    private sealed record TrackerDto(
        [property: JsonPropertyName("melds")] int Melds,
        [property: JsonPropertyName("deferred_ticks")] int DeferredTicks,
        [property: JsonPropertyName("pending_seat")] int PendingSeat,
        [property: JsonPropertyName("meld_akadora")] int MeldAkadora);

    private sealed record SeatDto(
        [property: JsonPropertyName("dc")] int Dc,
        [property: JsonPropertyName("d")] int[] D,
        [property: JsonPropertyName("m")] MeldDto[] M,
        [property: JsonPropertyName("r")] bool R,
        [property: JsonPropertyName("ri")] int Ri,
        [property: JsonPropertyName("ip")] bool Ip);

    private sealed record MeldDto(
        [property: JsonPropertyName("k")] string K,
        [property: JsonPropertyName("t")] int[] T,
        [property: JsonPropertyName("c")] int? C,
        [property: JsonPropertyName("fs")] int Fs);

    private sealed record CallPromptDto(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("variant")] string Variant,
        [property: JsonPropertyName("sc")] int StateCode,
        [property: JsonPropertyName("flags")] int Flags,
        [property: JsonPropertyName("pon")] int[] Pon,
        [property: JsonPropertyName("chi")] int[] Chi,
        [property: JsonPropertyName("kan")] int[] Kan,
        [property: JsonPropertyName("av")] int?[] Av);
}
