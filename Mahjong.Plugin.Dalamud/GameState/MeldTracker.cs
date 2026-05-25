using System;
using System.Collections.Generic;
using Mahjong.Core;

namespace Mahjong.Plugin.Dalamud.GameState;

/// <summary>Infers the player's open melds from closed-hand deltas (the Emj addon exposes no open-meld record).</summary>
public sealed class MeldTracker
{
    private const int WallJumpThreshold = 5;

    // Tolerate addon-memory write-ordering races between the hand-array and per-seat discard-count writes (observed 1–10 ticks).
    private const int MaxDeferralTicks = 30;

    private readonly List<Meld> melds = new();
    private readonly int[] lastDiscardCounts = new int[4];
    private int lastObservedWall = -1;
    private List<Tile>? lastHand;
    private int? lastAkadora;
    private int meldAkadora;
    // Opp discard fires several ticks before our chi/pon click — latch the most-recent opp discarder and consume it when the closed-hand shrink fires.
    private int pendingOppDiscardSeat = -1;
    private int deferredTicks;

    public IReadOnlyList<Meld> Melds => melds;

    public MeldTrackerStateDto SerializeState() => new(
        Melds: melds.Count,
        DeferredTicks: deferredTicks,
        PendingOppDiscardSeat: pendingOppDiscardSeat,
        MeldAkadora: meldAkadora,
        LastObservedWall: lastObservedWall);

    /// <summary>Akadora moved from closed hand into open melds; only captures self-hand reds — opponent-discard claims of red are undercounted.</summary>
    public int MeldAkadora => meldAkadora;

    /// <summary>Reserved for self-declared AnKan/ShouMinKan; for called melds, prefer ObserveSnapshot.</summary>
    public void Record(Meld meld) => melds.Add(meld);

    public void Clear()
    {
        melds.Clear();
        lastObservedWall = -1;
        lastHand = null;
        lastAkadora = null;
        meldAkadora = 0;
        pendingOppDiscardSeat = -1;
        deferredTicks = 0;
        Array.Clear(lastDiscardCounts);
    }

    /// <summary>Sharp upward jump in wall remaining = fresh hand dealt; reset all tracker state.</summary>
    public void ObserveWall(int wallRemaining)
    {
        if (lastObservedWall >= 0 && wallRemaining > lastObservedWall + WallJumpThreshold)
        {
            melds.Clear();
            lastHand = null;
            lastAkadora = null;
            meldAkadora = 0;
            pendingOppDiscardSeat = -1;
            deferredTicks = 0;
            Array.Clear(lastDiscardCounts);
        }
        lastObservedWall = wallRemaining;
    }

    /// <summary>Pass ourSeat=-1 when unknown — tracker refuses to infer rather than guess fromSeat (which corrupts fu/wait scoring).</summary>
    public Meld? ObserveSnapshot(
        IReadOnlyList<Tile> currentHand, int[] discardCounts, int ourSeat, int currentAkadora = 0)
    {
        ArgumentNullException.ThrowIfNull(currentHand);
        ArgumentNullException.ThrowIfNull(discardCounts);
        if (discardCounts.Length != 4)
            throw new ArgumentException("discardCounts must be length 4", nameof(discardCounts));
        if (ourSeat is < -1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(ourSeat));
        if (currentAkadora < 0)
            throw new ArgumentOutOfRangeException(nameof(currentAkadora));

        if (ourSeat < 0)
        {
            lastHand = new List<Tile>(currentHand);
            lastAkadora = currentAkadora;
            Array.Copy(discardCounts, lastDiscardCounts, 4);
            deferredTicks = 0;
            return null;
        }

        // If we discarded ourselves, active deferral baseline is stale — drop it.
        if (deferredTicks > 0 && lastHand is not null
            && discardCounts[ourSeat] > lastDiscardCounts[ourSeat])
        {
            deferredTicks = 0;
        }

        UpdatePendingOppDiscarder(discardCounts, ourSeat);

        Meld? inferred = null;
        if (lastHand is not null)
        {
            int delta = lastHand.Count - currentHand.Count;
            if (delta is 2 or 3)
            {
                var removed = DiffRemoved(lastHand, currentHand);
                if (removed.Count == delta)
                {
                    if (pendingOppDiscardSeat >= 0)
                    {
                        inferred = delta == 2
                            ? InferChiOrPon(removed, pendingOppDiscardSeat)
                            : InferMinKan(removed, pendingOppDiscardSeat);
                        if (inferred is { } m)
                        {
                            melds.Add(m);
                            if (lastAkadora is int prev)
                                meldAkadora += Math.Max(0, prev - currentAkadora);
                            pendingOppDiscardSeat = -1;
                            deferredTicks = 0;
                        }
                    }
                    else if (deferredTicks < MaxDeferralTicks)
                    {
                        // Closed hand shrunk in chi/pon/minkan shape but opp's discard-count byte hasn't propagated — retry next tick from the same baseline.
                        deferredTicks++;
                        return null;
                    }
                }
            }
            else
            {
                deferredTicks = 0;
            }
        }

        lastHand = new List<Tile>(currentHand);
        lastAkadora = currentAkadora;
        Array.Copy(discardCounts, lastDiscardCounts, 4);
        return inferred;
    }

    private void UpdatePendingOppDiscarder(int[] discardCounts, int ourSeat)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == ourSeat)
                continue;
            if (discardCounts[i] > lastDiscardCounts[i])
                pendingOppDiscardSeat = i;
        }
    }

    private static List<Tile> DiffRemoved(IReadOnlyList<Tile> before, IReadOnlyList<Tile> after)
    {
        var counts = new int[Tile.Count34];
        foreach (var t in before)
            counts[t.Id]++;
        foreach (var t in after)
            counts[t.Id]--;

        var removed = new List<Tile>();
        for (int id = 0; id < Tile.Count34; id++)
        {
            int c = counts[id];
            if (c <= 0)
                continue;
            for (int i = 0; i < c; i++)
                removed.Add(new Tile((byte)id));
        }
        return removed;
    }

    private static Meld? InferChiOrPon(List<Tile> removed, int fromSeat)
    {
        var a = removed[0];
        var b = removed[1];

        if (a.Id == b.Id)
            return Meld.Pon(a, a, fromSeat);

        if (a.Suit == TileSuit.Honor || a.Suit != b.Suit)
            return null;
        int diff = b.Id - a.Id;
        if (diff is not (1 or 2))
            return null;

        // diff=2: called tile is the middle (low=a). diff=1: ambiguous, prefer down-extension when in-suit.
        Tile low;
        if (diff == 2)
        {
            low = a;
        }
        else
        {
            var down = new Tile((byte)(a.Id - 1));
            low = (a.Id > 0 && down.Suit == a.Suit) ? down : a;
        }

        Tile called = FindCalledTile(low, a, b);
        return Meld.Chi(low, called, fromSeat);
    }

    private static Tile FindCalledTile(Tile low, Tile a, Tile b)
    {
        var t0 = low;
        var t1 = new Tile((byte)(low.Id + 1));
        var t2 = new Tile((byte)(low.Id + 2));
        if (t0.Id != a.Id && t0.Id != b.Id)
            return t0;
        if (t1.Id != a.Id && t1.Id != b.Id)
            return t1;
        return t2;
    }

    private static Meld? InferMinKan(List<Tile> removed, int fromSeat)
    {
        if (removed[0].Id == removed[1].Id && removed[1].Id == removed[2].Id)
            return Meld.MinKan(removed[0], removed[0], fromSeat);
        return null;
    }
}

public readonly record struct MeldTrackerStateDto(
    int Melds,
    int DeferredTicks,
    int PendingOppDiscardSeat,
    int MeldAkadora,
    int LastObservedWall);
