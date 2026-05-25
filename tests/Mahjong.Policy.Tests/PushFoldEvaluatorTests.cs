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

    private static ScoredDiscard Cut(
        int tileId, int shanten, double dealInCost = 0,
        int ukeireWeighted = 8, double yakuPotential = 0.5, int dora = 0)
        => new(Tile.FromId(tileId), Score: 0, ShantenAfter: shanten,
               UkeireKinds: 0, UkeireWeighted: ukeireWeighted,
               DoraRetained: dora, YakuhaiRetained: 0, DealInCost: dealInCost,
               YakuPotential: yakuPotential);

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
    public void Tenpai_with_huge_danger_folds()
    {
        var s = BaseState();
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 4, shanten: 0, dealInCost: 9000));
        Assert.Equal(PushFoldStance.Fold, d.Value);
    }

    [Fact]
    public void Two_shanten_late_game_with_danger_folds_on_ev()
    {
        var s = BaseState(wallRemaining: 8);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model,
            Cut(tileId: 0, shanten: 2, ukeireWeighted: 4, yakuPotential: 0.3, dealInCost: 2500));
        Assert.Equal(PushFoldStance.Fold, d.Value);
    }

    [Fact]
    public void Three_shanten_vs_riichi_hard_vetoes()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], true, 5, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = BaseState(wallRemaining: 30, seats: seats);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 0, shanten: 3));
        Assert.Equal(PushFoldStance.Fold, d.Value);
        Assert.Equal("far-vs-riichi", d.Reason.Code);
    }

    [Fact]
    public void Two_shanten_early_with_value_pushes()
    {
        var s = BaseState(wallRemaining: 55);
        var model = new OpponentModel();
        model.Update(s);
        var d = Policy.Evaluate(s, model, Cut(tileId: 0, shanten: 2, yakuPotential: 0.8, dora: 2));
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
        Assert.NotNull(d.Reason);
        Assert.True(d.Value is PushFoldStance.Push or PushFoldStance.Fold);
    }
}
