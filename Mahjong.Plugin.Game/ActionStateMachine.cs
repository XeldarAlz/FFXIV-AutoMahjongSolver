using Mahjong.Core;

namespace Mahjong.Plugin.Game;

/// <summary>
/// Pure state for the auto-play tick loop. Invariants: <see cref="BeginDispatch"/> must
/// be paired with <see cref="CompleteDispatch"/> in a finally; <see cref="LatchRiichiConfirm"/>
/// is one-shot and self-clears on hand transition.
/// </summary>
public sealed class ActionStateMachine
{
    private readonly TimeSpan dispatchTimeout;
    private readonly TimeSpan retryCooldown;

    private bool inFlight;
    private DateTime dispatchStartedAt;
    private DateTime lastActionAt;
    private DispatchContext? lastContext;
    private bool riichiConfirmLatched;
    private Tile? riichiConfirmTile;
    private int lastObservedWall = -1;

    public ActionStateMachine(TimeSpan dispatchTimeout, TimeSpan retryCooldown)
    {
        this.dispatchTimeout = dispatchTimeout;
        this.retryCooldown = retryCooldown;
    }

    public bool IsDispatchInFlight => inFlight;

    /// <summary>True when the riichi-accept popup may still be visible from a previous dispatch.</summary>
    public bool IsRiichiConfirmPending => riichiConfirmLatched;

    /// <summary>
    /// Tile the policy chose for the post-riichi tsumogiri; null when the latch was set via a
    /// probe-accept path without an explicit tile decision.
    /// </summary>
    public Tile? RiichiConfirmTile => riichiConfirmTile;

    public bool TryRecoverFromStuckDispatch(DateTime now)
    {
        if (!inFlight)
            return false;
        if (now - dispatchStartedAt <= dispatchTimeout)
            return false;
        inFlight = false;
        return true;
    }

    public void BeginDispatch(DateTime now, DispatchContext context)
    {
        inFlight = true;
        dispatchStartedAt = now;
        lastActionAt = now;
        lastContext = context;
    }

    public void CompleteDispatch() => inFlight = false;

    public bool ShouldSuppressForContext(DispatchContext context, DateTime now)
        => lastContext.HasValue
           && lastContext.Value.Equals(context)
           && now - lastActionAt < retryCooldown;

    public void ClearContext() => lastContext = null;

    /// <summary>A null <paramref name="target"/> preserves any previously-latched tile.</summary>
    public void LatchRiichiConfirm(Tile? target = null)
    {
        riichiConfirmLatched = true;
        if (target is not null)
            riichiConfirmTile = target;
    }

    public void ClearRiichiConfirm()
    {
        riichiConfirmLatched = false;
        riichiConfirmTile = null;
    }

    /// <summary>
    /// Sharp upward wall jump = new hand dealt → clear per-hand state. Tolerance 5 absorbs
    /// transient wall-read glitches.
    /// </summary>
    public void ObserveWall(int wall)
    {
        if (lastObservedWall >= 0 && wall > lastObservedWall + 5)
        {
            riichiConfirmLatched = false;
            riichiConfirmTile = null;
            lastContext = null;
        }
        lastObservedWall = wall;
    }
}

public readonly record struct DispatchContext(int State, int Hand);
