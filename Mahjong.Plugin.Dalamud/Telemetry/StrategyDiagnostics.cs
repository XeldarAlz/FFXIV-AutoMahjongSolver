using System;
using System.Collections.Generic;
using Mahjong.Core;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.Logging;
using Mahjong.Plugin.Game;
using Mahjong.Policy.Abstractions;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Plugin.Dalamud.Telemetry;

/// <summary>
/// Emits per-turn diagnostic findings to answer three loss-mode hypotheses:
/// (1) was the policy's recommendation actually played, (2) how often do we sit
/// in yakuless tenpai under Doman's 2-han minimum, (3) what reasons does the
/// call policy report. Closes each hand with a roll-up so 10-game patterns
/// show up without re-grepping every decision line.
/// </summary>
public sealed class StrategyDiagnostics : IDisposable
{
    private const ActionFlags CallOfferFlags =
        ActionFlags.Pon | ActionFlags.Chi |
        ActionFlags.AnKan | ActionFlags.MinKan | ActionFlags.ShouMinKan;

    private static readonly HashSet<string> CallStepCodes = new(StringComparer.Ordinal)
    {
        "shanten-gain", "no-shanten-gain-with-yaku", "no-candidate",
    };

    private readonly StateAggregator aggregator;
    private readonly IDiscardCapture capture;
    private readonly IFindingsLog findings;
    private readonly object gate = new();

    private Tile? lastRecommendedTile;
    private DateTime lastRecommendedAtUtc;

    // Tile-count snapshot of our hand at last seen decision; used to detect a
    // self-discard by delta when IDiscardCapture isn't producing seat-attributed
    // events (on this user's environment the native-asm hook emits no discards).
    private int[]? lastHandCounts;
    private int lastHandTotal = -1;
    private int lastMeldsCount = -1;

    private long lastEmittedKey = long.MinValue;

    private int lastDealerSeat = -1;
    private int lastWall = -1;
    private int handTurns;
    private int handYakulessTenpai;
    private int handCallsOffered;
    private int handCallsAccepted;
    private int handRecMatch;
    private int handRecMismatch;
    private readonly Dictionary<string, int> handCallReasons = new(StringComparer.Ordinal);

    private bool disposed;

    public StrategyDiagnostics(
        StateAggregator aggregator, IDiscardCapture capture, IFindingsLog findings)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(findings);
        this.aggregator = aggregator;
        this.capture = capture;
        this.findings = findings;

        aggregator.Changed += OnAggregatorChanged;
        capture.DiscardObserved += OnDiscardObserved;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        aggregator.Changed -= OnAggregatorChanged;
        capture.DiscardObserved -= OnDiscardObserved;
    }

    private void OnAggregatorChanged(StateSnapshot snap)
    {
        if (disposed)
            return;

        var choice = aggregator.LastChoice;
        var scored = aggregator.LastScored;

        lock (gate)
        {
            CheckSelfDiscardByDelta(snap);
            DetectHandBoundary(snap);

            if (choice is null)
                return;

            long key = ComputeDedupKey(snap, choice);
            if (key == lastEmittedKey)
                return;
            lastEmittedKey = key;

            ScoredDiscard? top = scored is { Length: > 0 } ? scored[0] : null;
            double projectedHan = top.HasValue ? top.Value.YakuPotential * YakuPotential.TargetHan : 0.0;
            bool yakulessTenpai = top is { ShantenAfter: 0 } && projectedHan < 2.0;
            if (yakulessTenpai)
                handYakulessTenpai++;

            if (choice.Kind is ActionKind.Discard or ActionKind.Riichi
                && choice.DiscardTile is { } recTile)
            {
                lastRecommendedTile = recTile;
                lastRecommendedAtUtc = DateTime.UtcNow;
                handTurns++;
            }

            bool callOffered = (snap.Legal.Flags & CallOfferFlags) != 0;
            string? callCode = null;
            if (callOffered)
            {
                handCallsOffered++;
                if (choice.Kind is ActionKind.Pon or ActionKind.Chi
                    or ActionKind.AnKan or ActionKind.MinKan or ActionKind.ShouMinKan)
                    handCallsAccepted++;
                callCode = ExtractCallReasonCode(choice);
                if (callCode is not null)
                    handCallReasons[callCode] = handCallReasons.GetValueOrDefault(callCode) + 1;
            }

            findings.Record("policy_diagnostic", new Dictionary<string, object?>
            {
                ["turn"] = snap.TurnIndex,
                ["wall"] = snap.WallRemaining,
                ["dealer"] = snap.DealerSeat,
                ["our_seat"] = snap.OurSeat,
                ["round_wind"] = snap.RoundWind,
                ["legal"] = snap.Legal.Flags.ToString(),
                ["hand_count"] = snap.Hand.Count,
                ["our_melds"] = snap.OurMelds.Count,
                ["choice_kind"] = choice.Kind.ToString(),
                ["choice_tile"] = choice.DiscardTile?.ToString(),
                ["reasoning"] = choice.Reasoning,
                ["top_shanten_after"] = top?.ShantenAfter,
                ["top_ukeire_kinds"] = top?.UkeireKinds,
                ["top_ukeire_weighted"] = top?.UkeireWeighted,
                ["top_dora"] = top?.DoraRetained,
                ["top_yakuhai"] = top?.YakuhaiRetained,
                ["top_yaku_potential"] = top?.YakuPotential,
                ["top_deal_in_cost"] = top?.DealInCost,
                ["projected_han"] = projectedHan,
                ["yakuless_tenpai"] = yakulessTenpai,
                ["call_offered"] = callOffered,
                ["call_reason_code"] = callCode,
            });
        }
    }

    private void OnDiscardObserved(DiscardEvent evt)
    {
        if (disposed)
            return;

        var snap = aggregator.Latest;
        if (snap is null)
            return;
        if (evt.Seat < 0 || evt.Seat != snap.OurSeat)
            return;

        lock (gate)
        {
            EmitRecCheck(evt.Tile, source: "capture",
                latencyMs: (int)(DateTime.UtcNow - lastRecommendedAtUtc).TotalMilliseconds,
                obsSeq: (long?)evt.SequenceNumber);
        }
    }

    /// <summary>
    /// Fallback rec-match when <see cref="IDiscardCapture"/> isn't emitting seat-attributed
    /// events (the native-asm hook is dormant on this build). Compares previous-snapshot tile
    /// counts to current; when exactly one tile-id dropped by one and no meld grew, that's our
    /// self-discard. Detection runs BEFORE updating <c>lastHandCounts</c> so the very next call
    /// reads the fresh baseline.
    /// </summary>
    private void CheckSelfDiscardByDelta(StateSnapshot snap)
    {
        int total = snap.Hand.Count;
        int meldsCount = snap.OurMelds.Count;

        var counts = new int[Tile.Count34];
        foreach (var t in snap.Hand)
            counts[t.Id]++;

        if (lastHandCounts is not null
            && lastHandTotal == total + 1
            && lastMeldsCount == meldsCount
            && lastRecommendedTile is not null)
        {
            int droppedId = -1;
            int diffs = 0;
            for (int i = 0; i < Tile.Count34; i++)
            {
                int d = lastHandCounts[i] - counts[i];
                if (d == 0)
                    continue;
                if (d == 1 && droppedId < 0)
                {
                    droppedId = i;
                    diffs++;
                }
                else
                {
                    diffs = -1;
                    break;
                }
            }
            if (diffs == 1 && droppedId >= 0)
            {
                var actual = Tile.FromId(droppedId);
                EmitRecCheck(actual, source: "hand-delta",
                    latencyMs: (int)(DateTime.UtcNow - lastRecommendedAtUtc).TotalMilliseconds,
                    obsSeq: null);
            }
        }

        lastHandCounts = counts;
        lastHandTotal = total;
        lastMeldsCount = meldsCount;
    }

    private void EmitRecCheck(Tile actual, string source, int latencyMs, long? obsSeq)
    {
        if (lastRecommendedTile is not { } rec)
            return;
        bool match = actual.Id == rec.Id;
        if (match)
            handRecMatch++;
        else
            handRecMismatch++;

        findings.Record("recommendation_check", new Dictionary<string, object?>
        {
            ["source"] = source,
            ["recommended"] = rec.ToString(),
            ["recommended_id"] = rec.Id,
            ["actual"] = actual.ToString(),
            ["actual_id"] = actual.Id,
            ["match"] = match,
            ["latency_ms"] = latencyMs,
            ["obs_seq"] = obsSeq,
        });

        lastRecommendedTile = null;
    }

    private void DetectHandBoundary(StateSnapshot snap)
    {
        int dealer = snap.DealerSeat;
        int wall = snap.WallRemaining;

        if (lastDealerSeat < 0)
        {
            lastDealerSeat = dealer;
            lastWall = wall;
            return;
        }

        bool dealerRotated = dealer != lastDealerSeat;
        bool wallReset = wall > lastWall + 5;
        if (dealerRotated || wallReset)
        {
            EmitHandSummary(boundary: dealerRotated ? "dealer-rotated" : "wall-reset",
                snap: snap);
            ResetHandCounters();
        }

        lastDealerSeat = dealer;
        lastWall = wall;
    }

    private void EmitHandSummary(string boundary, StateSnapshot snap)
    {
        var reasons = new Dictionary<string, object?>(handCallReasons.Count);
        foreach (var kv in handCallReasons)
            reasons[kv.Key] = kv.Value;

        findings.Record("hand_summary", new Dictionary<string, object?>
        {
            ["boundary"] = boundary,
            ["dealer_new"] = snap.DealerSeat,
            ["round_wind"] = snap.RoundWind,
            ["wall_now"] = snap.WallRemaining,
            ["turns_observed"] = handTurns,
            ["yakuless_tenpai_decisions"] = handYakulessTenpai,
            ["calls_offered"] = handCallsOffered,
            ["calls_accepted"] = handCallsAccepted,
            ["rec_match"] = handRecMatch,
            ["rec_mismatch"] = handRecMismatch,
            ["call_reasons"] = reasons,
        });
    }

    private void ResetHandCounters()
    {
        handTurns = 0;
        handYakulessTenpai = 0;
        handCallsOffered = 0;
        handCallsAccepted = 0;
        handRecMatch = 0;
        handRecMismatch = 0;
        handCallReasons.Clear();
        lastRecommendedTile = null;
        lastEmittedKey = long.MinValue;
    }

    /// <summary>
    /// Dedup key. <c>TurnIndex</c> is always 0 on this user's snapshots, so the key relies
    /// on wall + hand + meld + flags + kind + tile to distinguish successive turns.
    /// </summary>
    private static long ComputeDedupKey(StateSnapshot snap, ActionChoice choice)
    {
        unchecked
        {
            long key = (long)(uint)snap.WallRemaining;
            key = (key * 397) ^ (long)(uint)snap.Hand.Count;
            key = (key * 397) ^ (long)(uint)snap.OurMelds.Count;
            key = (key * 397) ^ (long)(uint)snap.Legal.Flags;
            key = (key * 397) ^ (byte)choice.Kind;
            key = (key * 397) ^ (choice.DiscardTile?.Id ?? 0xFF);
            return key;
        }
    }

    private static string? ExtractCallReasonCode(ActionChoice choice)
    {
        foreach (var step in choice.ReasonSteps)
            if (CallStepCodes.Contains(step.Code))
                return step.Code;
        return null;
    }
}
