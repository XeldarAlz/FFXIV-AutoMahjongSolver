namespace Mahjong.Plugin.Game.Tests;

public class ActionStateMachineTests
{
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(3);

    private static ActionStateMachine NewFsm() => new(DispatchTimeout, RetryCooldown);

    private static readonly DispatchContext Ctx = new(State: 30, Hand: 14);
    private static readonly DispatchContext OtherCtx = new(State: 15, Hand: 13);

    [Fact]
    public void Initial_state_is_idle()
    {
        var fsm = NewFsm();
        Assert.False(fsm.IsDispatchInFlight);
        Assert.False(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void BeginDispatch_then_CompleteDispatch_returns_to_idle()
    {
        var fsm = NewFsm();
        fsm.BeginDispatch(DateTime.UtcNow, Ctx);
        Assert.True(fsm.IsDispatchInFlight);
        fsm.CompleteDispatch();
        Assert.False(fsm.IsDispatchInFlight);
    }

    [Fact]
    public void Stuck_dispatch_not_recovered_within_timeout()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        Assert.False(fsm.TryRecoverFromStuckDispatch(t0 + TimeSpan.FromSeconds(5)));
        Assert.True(fsm.IsDispatchInFlight);
    }

    [Fact]
    public void Stuck_dispatch_recovered_after_timeout()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        Assert.True(fsm.TryRecoverFromStuckDispatch(t0 + DispatchTimeout + TimeSpan.FromSeconds(1)));
        Assert.False(fsm.IsDispatchInFlight);
    }

    [Fact]
    public void TryRecoverFromStuckDispatch_returns_false_when_idle()
    {
        var fsm = NewFsm();
        Assert.False(fsm.TryRecoverFromStuckDispatch(DateTime.UtcNow));
    }

    [Fact]
    public void Same_context_within_cooldown_is_suppressed()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        fsm.CompleteDispatch();

        Assert.True(fsm.ShouldSuppressForContext(Ctx, t0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Same_context_after_cooldown_is_not_suppressed()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        fsm.CompleteDispatch();

        Assert.False(fsm.ShouldSuppressForContext(Ctx, t0 + RetryCooldown + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Different_context_is_never_suppressed()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        fsm.CompleteDispatch();

        Assert.False(fsm.ShouldSuppressForContext(OtherCtx, t0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Suppression_requires_a_recorded_context()
    {
        var fsm = NewFsm();
        Assert.False(fsm.ShouldSuppressForContext(Ctx, DateTime.UtcNow));
    }

    [Fact]
    public void ClearContext_drops_the_recorded_context()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;
        fsm.BeginDispatch(t0, Ctx);
        fsm.CompleteDispatch();
        fsm.ClearContext();

        Assert.False(fsm.ShouldSuppressForContext(Ctx, t0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void RiichiConfirm_latches_on_and_off()
    {
        var fsm = NewFsm();
        Assert.False(fsm.IsRiichiConfirmPending);

        fsm.LatchRiichiConfirm();
        Assert.True(fsm.IsRiichiConfirmPending);

        fsm.ClearRiichiConfirm();
        Assert.False(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void RiichiConfirm_latch_is_independent_from_dispatch_flag()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm();
        fsm.BeginDispatch(DateTime.UtcNow, Ctx);
        fsm.CompleteDispatch();

        Assert.True(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void ObserveWall_does_not_clear_latch_within_a_hand()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm();
        fsm.ObserveWall(70);
        fsm.ObserveWall(45);
        fsm.ObserveWall(30);
        fsm.ObserveWall(10);
        Assert.True(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void ObserveWall_clears_latch_on_hand_transition()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm();
        fsm.ObserveWall(20);
        fsm.ObserveWall(70);
        Assert.False(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void ObserveWall_tolerates_minor_wall_jitter()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm();
        fsm.ObserveWall(20);
        fsm.ObserveWall(24);
        fsm.ObserveWall(22);
        Assert.True(fsm.IsRiichiConfirmPending);
    }

    [Fact]
    public void RiichiConfirm_stores_the_policy_chosen_tile()
    {
        var fsm = NewFsm();
        var tile = Tile.FromId(5);
        fsm.LatchRiichiConfirm(tile);

        Assert.True(fsm.IsRiichiConfirmPending);
        Assert.Equal(tile, fsm.RiichiConfirmTile);
    }

    [Fact]
    public void RiichiConfirm_subsequent_null_latch_keeps_original_tile()
    {
        // Pins that a re-latch with null tile keeps the original policy-chosen tile across the post-riichi yaku-preview confirm.
        var fsm = NewFsm();
        var tile = Tile.FromId(11);
        fsm.LatchRiichiConfirm(tile);
        fsm.LatchRiichiConfirm(null);

        Assert.Equal(tile, fsm.RiichiConfirmTile);
    }

    [Fact]
    public void ClearRiichiConfirm_drops_the_tile()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm(Tile.FromId(20));
        fsm.ClearRiichiConfirm();

        Assert.Null(fsm.RiichiConfirmTile);
    }

    [Fact]
    public void ObserveWall_hand_transition_clears_the_tile()
    {
        var fsm = NewFsm();
        fsm.LatchRiichiConfirm(Tile.FromId(7));
        fsm.ObserveWall(20);
        fsm.ObserveWall(70);

        Assert.Null(fsm.RiichiConfirmTile);
    }

    [Fact]
    public void Cooldown_resets_when_a_new_dispatch_for_same_context_completes()
    {
        var fsm = NewFsm();
        var t0 = DateTime.UtcNow;

        fsm.BeginDispatch(t0, Ctx);
        fsm.CompleteDispatch();

        var afterCooldown = t0 + RetryCooldown + TimeSpan.FromSeconds(1);
        Assert.False(fsm.ShouldSuppressForContext(Ctx, afterCooldown));

        fsm.BeginDispatch(afterCooldown, Ctx);
        fsm.CompleteDispatch();

        Assert.True(fsm.ShouldSuppressForContext(Ctx, afterCooldown + TimeSpan.FromSeconds(1)));
    }
}
