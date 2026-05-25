using System;
using Dalamud.Plugin.Services;
using Mahjong.Core;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Hooks.Strategies;

public sealed class AddonPollDiscardCapture : IDiscardCapture
{
    public const string Name = "addon-poll";

    private readonly int[] lastDiscardCounts = new int[4];
    private bool primed;
    private ulong totalCaptured;
    private int lastTileId = -1;
    private bool disposed;

    public HookHealth Health { get; } = HookHealth.Fallback;
    public string StrategyName => Name;
    public ulong TotalCaptured => totalCaptured;
    public int LastTileId => lastTileId;
    public event Action<DiscardEvent>? DiscardObserved;

    public AddonPollDiscardCapture(IPluginLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        log.Info(
            "[DiscardCapture/addon-poll] active — inferring discards from " +
            "StateAggregator snapshots (native asm hook unavailable).");
    }

    /// <summary>First snapshot primes the counters to avoid flooding on a mid-hand plugin load.</summary>
    public void OnSnapshotChanged(StateSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        if (disposed)
            return;

        var seats = snap.Seats;
        if (!primed)
        {
            for (int i = 0; i < 4 && i < seats.Count; i++)
                lastDiscardCounts[i] = seats[i].Discards.Count;
            primed = true;
            return;
        }

        var now = DateTime.UtcNow;
        for (int seat = 0; seat < 4 && seat < seats.Count; seat++)
        {
            var discards = seats[seat].Discards;
            int prev = lastDiscardCounts[seat];
            int curr = discards.Count;

            if (curr < prev)
            {
                lastDiscardCounts[seat] = curr;
                continue;
            }
            for (int i = prev; i < curr; i++)
                EmitDiscard(seat, discards[i], now);
            lastDiscardCounts[seat] = curr;
        }
    }

    private void EmitDiscard(int seat, Tile tile, DateTime now)
    {
        totalCaptured++;
        lastTileId = tile.Id;
        DiscardObserved?.Invoke(new DiscardEvent(
            Seat: seat,
            Tile: tile,
            ObservedAtUtc: now,
            SequenceNumber: totalCaptured));
    }

    public void Dispose()
    {
        disposed = true;
    }
}
