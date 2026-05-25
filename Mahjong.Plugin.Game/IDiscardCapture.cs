using Mahjong.Core;

namespace Mahjong.Plugin.Game;

public enum HookHealth
{
    Active,
    Fallback,
    Offline,
}

/// <summary>Subscribers run on the framework thread — implementations marshal to it before firing.</summary>
public interface IDiscardCapture : IDisposable
{
    HookHealth Health { get; }

    /// <summary>"native-asm", "addon-poll", "inert".</summary>
    string StrategyName { get; }

    ulong TotalCaptured { get; }

    /// <summary>-1 if none yet.</summary>
    int LastTileId { get; }

    event Action<DiscardEvent>? DiscardObserved;
}

/// <param name="Seat">-1 when the strategy can't attribute (native-asm sees a pool address only).</param>
public readonly record struct DiscardEvent(
    int Seat, Tile Tile, DateTime ObservedAtUtc, ulong SequenceNumber);
