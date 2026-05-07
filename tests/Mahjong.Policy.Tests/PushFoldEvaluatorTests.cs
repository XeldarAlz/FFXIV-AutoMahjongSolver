using Mahjong.Policy.Efficiency;
using Mahjong.Policy.Opponents;

namespace Mahjong.Policy.Tests;

public class PushFoldEvaluatorTests
{
    private static readonly HeuristicPushFoldPolicy Policy = new();

    private static StateSnapshot BaseState(int wallRemaining = 40, SeatView[]? seats = null)
    {
        seats ??=
        [
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
            new SeatView([], [], [], false, -1, false, false),
        ];
        return StateSnapshot.Empty with
        {
            WallRemaining = wallRemaining,
            Seats = seats,
        };
    }

    private static ScoredDiscard Cut(int tileId, int shanten, double dealInCost = 0)
        => new(Tile.FromId(tileId), Score: 0, ShantenAfter: shanten,
               UkeireKinds: 0, UkeireWeighted: 0,
               DoraRetained: 0, YakuhaiRetained: 0, DealInCost: dealInCost);

    [Fact]
    public void Tenpai_with_low_danger_pushes()
    {
        var s = BaseState();
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 4, shanten: 0));
        Assert.Equal(PushFoldStance.Push, d.Value);
    }

    [Fact]
    public void Two_shanten_late_game_folds()
    {
        var s = BaseState(wallRemaining: 8);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 0, shanten: 2));
        Assert.Equal(PushFoldStance.Fold, d.Value);
        Assert.Equal("late-round-far", d.Reason.Code);
    }

    [Fact]
    public void Two_shanten_vs_riichi_folds()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], true, 5, false, false),    // shimocha riichi
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = BaseState(wallRemaining: 30, seats: seats);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 0, shanten: 2));
        Assert.Equal(PushFoldStance.Fold, d.Value);
        Assert.Equal("far-vs-riichi", d.Reason.Code);
    }

    [Fact]
    public void Two_shanten_early_game_pushes()
    {
        var s = BaseState(wallRemaining: 55);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 0, shanten: 2));
        Assert.Equal(PushFoldStance.Push, d.Value);
    }

    [Fact]
    public void One_shanten_runs_without_error()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], true, 3, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = BaseState(wallRemaining: 35, seats: seats);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 5, shanten: 1));
        // Decision can land either way depending on exact danger — just verify it ran.
        Assert.NotNull(d.Reason);
        Assert.True(d.Value is PushFoldStance.Push or PushFoldStance.Fold);
    }
}
