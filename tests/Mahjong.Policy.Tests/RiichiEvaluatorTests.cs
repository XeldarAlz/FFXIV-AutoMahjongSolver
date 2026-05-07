using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tests;

public class RiichiEvaluatorTests
{
    private static readonly Tile Anchor2m = Tiles.Parse("2m")[0];
    private static readonly HeuristicRiichiPolicy Policy = new();

    private static StateSnapshot BaseState(int wallRemaining = 40, int[]? scores = null)
    {
        var seats = new SeatView[4];
        for (int i = 0; i < 4; i++)
            seats[i] = new SeatView([], [], [], false, -1, false, false);
        return StateSnapshot.Empty with
        {
            Scores = scores ?? [25000, 25000, 25000, 25000],
            WallRemaining = wallRemaining,
            Seats = seats,
        };
    }

    private static ScoredDiscard PlannedDiscard(int shantenAfter, int ukeireWeighted, int ukeireKinds = 2)
        => new(Anchor2m, Score: 0, ShantenAfter: shantenAfter,
               UkeireKinds: ukeireKinds, UkeireWeighted: ukeireWeighted,
               DoraRetained: 0, YakuhaiRetained: 0, DealInCost: 0);

    [Fact]
    public void Declares_when_all_conditions_met()
    {
        var d = Policy.Evaluate(BaseState(), PlannedDiscard(shantenAfter: 0, ukeireWeighted: 8));
        Assert.True(d.Accept);
        Assert.True(d.Value);
    }

    [Fact]
    public void Rejects_when_not_tenpai()
    {
        var d = Policy.Evaluate(BaseState(), PlannedDiscard(shantenAfter: 1, ukeireWeighted: 8));
        Assert.False(d.Accept);
        Assert.Equal("not-tenpai", d.Reason.Code);
    }

    [Fact]
    public void Rejects_when_score_too_low()
    {
        var s = BaseState(scores: [500, 30000, 30000, 39500]);
        var d = Policy.Evaluate(s, PlannedDiscard(shantenAfter: 0, ukeireWeighted: 8));
        Assert.False(d.Accept);
        Assert.Equal("low-score", d.Reason.Code);
    }

    [Fact]
    public void Rejects_when_wall_nearly_empty()
    {
        var d = Policy.Evaluate(BaseState(wallRemaining: 3), PlannedDiscard(shantenAfter: 0, ukeireWeighted: 8));
        Assert.False(d.Accept);
        Assert.Equal("late-round", d.Reason.Code);
    }

    [Fact]
    public void Rejects_when_ukeire_too_thin()
    {
        var d = Policy.Evaluate(BaseState(),
            PlannedDiscard(shantenAfter: 0, ukeireWeighted: 2, ukeireKinds: 1));
        Assert.False(d.Accept);
        Assert.Equal("thin-waits", d.Reason.Code);
    }

    [Fact]
    public void Rejects_when_hand_is_open()
    {
        var pon = Meld.Pon(Tile.FromId(0), Tile.FromId(0), fromSeat: 2);
        var s = BaseState() with { OurMelds = [pon] };
        var d = Policy.Evaluate(s, PlannedDiscard(0, 8));
        Assert.False(d.Accept);
        Assert.Equal("hand-open", d.Reason.Code);
    }

    [Fact]
    public void Ankan_does_not_count_as_open()
    {
        var ankan = Meld.AnKan(Tile.FromId(0));
        var s = BaseState() with { OurMelds = [ankan] };
        var d = Policy.Evaluate(s, PlannedDiscard(0, 8));
        Assert.True(d.Accept);
    }
}
