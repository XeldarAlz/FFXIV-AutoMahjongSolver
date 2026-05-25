using Mahjong.Engine;
using Mahjong.Policy.Efficiency;
using Mahjong.Rules.Rulesets;
using Xunit;

namespace Mahjong.Policy.Tests;

public class EfficiencyPolicyTests
{
    private static readonly EfficiencyPolicy Policy = new();

    [Fact]
    public void Tsumo_accepted_when_hand_clears_min_han()
    {
        var s = Snapshots.Closed14("123m456p789s11123p", ActionFlags.Tsumo | ActionFlags.Discard);
        var choice = Policy.Choose(s);
        Assert.Equal(ActionKind.Tsumo, choice.Kind);
    }

    [Fact]
    public void Tsumo_declined_when_doman_min_han_not_met()
    {
        // Issue #51: yakuless complete shape under Doman MinHan=2.
        var doman = new EfficiencyPolicy(new DomanRuleSet());
        var s = Snapshots.Closed14("22666p123345s222z", ActionFlags.Tsumo | ActionFlags.Discard);
        var choice = doman.Choose(s);
        Assert.NotEqual(ActionKind.Tsumo, choice.Kind);
        Assert.Equal(ActionKind.Discard, choice.Kind);
    }

    [Fact]
    public void Ron_legal_returns_ron()
    {
        var s = Snapshots.Closed14("123m456p789s11123p", ActionFlags.Ron);
        var choice = Policy.Choose(s);
        Assert.Equal(ActionKind.Ron, choice.Kind);
    }

    [Fact]
    public void Pon_opportunity_passes_for_now()
    {
        var s = Snapshots.Closed14("123m456p789s234s55m", ActionFlags.Pon | ActionFlags.Pass);
        var choice = Policy.Choose(s);
        Assert.Equal(ActionKind.Pass, choice.Kind);
    }

    [Fact]
    public void Best_discard_does_not_regress_shanten()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var choice = Policy.Choose(s);

        Assert.Equal(ActionKind.Discard, choice.Kind);
        Assert.NotNull(choice.DiscardTile);

        var chosen = choice.DiscardTile!.Value;
        Assert.True(chosen.Id is 31 or 32,
            $"expected 5z (31) or 6z (32), got {chosen} (id {chosen.Id}). Reasoning: {choice.Reasoning}");
    }

    [Fact]
    public void Discard_scorer_sorts_options_best_first()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var scored = DiscardScorer.Score(s);
        Assert.NotEmpty(scored);
        for (int i = 1; i < scored.Length; i++)
            Assert.True(scored[i - 1].Score >= scored[i].Score,
                $"scored array not sorted: [{i - 1}]={scored[i - 1].Score} < [{i}]={scored[i].Score}");
    }

    [Fact]
    public void Dora_increases_score_of_cuts_that_retain_dora_tiles()
    {
        var noDora = Snapshots.Closed14("123m456p789s234s1m5m");
        var withDora = noDora with { DoraIndicators = [Tiles.Parse("4m")[0]] };

        double cut1mNoDora = DiscardScorer.Score(noDora).First(x => x.Discard.Id == 0).Score;
        double cut1mWithDora = DiscardScorer.Score(withDora).First(x => x.Discard.Id == 0).Score;

        Assert.True(cut1mWithDora > cut1mNoDora,
            $"dora indicator should bump the score of a cut that retains a dora tile. " +
            $"noDora={cut1mNoDora} withDora={cut1mWithDora}");

        var cut1m = DiscardScorer.Score(withDora).First(x => x.Discard.Id == 0);
        Assert.Equal(1, cut1m.DoraRetained);
    }

    [Fact]
    public void Isolated_honor_scores_higher_to_discard_than_connected_terminal()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z9p");
        var scored = DiscardScorer.Score(s);
        Assert.NotEmpty(scored);
        Assert.Equal(ActionKind.Discard, Policy.Choose(s).Kind);
    }

    [Fact]
    public void Rejects_non_14_tile_hands()
    {
        var s = Snapshots.Closed14("123m456p789s234s55z");
        var thirteenTile = s with { Hand = Tiles.Parse("123m456p789s234s5z") };
        Assert.Throws<ArgumentException>(() => DiscardScorer.Score(thirteenTile));
    }

    /// <summary>State-6 SelfDeclareList popup (hand=14, Kan + Discard both legal). Declining the kan must fall through to discard scoring — returning Pass softlocks the addon (no Pass button on the list widget). Reproduces 2026-05-26 log capture: hand 223679m 88p 118888s, addon offered MinKan flag for AnKan on 8s.</summary>
    [Fact]
    public void Declined_kan_at_state_6_returns_discard_not_pass()
    {
        var hand = Tiles.Parse("223679m88p118888s");
        var seats = new SeatView[4];
        for (int i = 0; i < 4; i++)
            seats[i] = new SeatView([], [], [], false, -1, false, false);

        var kanCand = new MeldCandidate(
            MeldKind.AnKan,
            Tile.FromId(25),
            [Tile.FromId(25), Tile.FromId(25), Tile.FromId(25)],
            FromSeat: 0);

        var s = StateSnapshot.Empty with
        {
            Hand = hand,
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Discard | ActionFlags.MinKan | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [],
                KanCandidates: [kanCand]),
        };

        var choice = Policy.Choose(s);
        Assert.NotEqual(ActionKind.Pass, choice.Kind);
        Assert.True(choice.Kind is ActionKind.Discard or ActionKind.AnKan,
            $"expected Discard or AnKan, got {choice.Kind} (reason: {choice.Reasoning})");
    }
}
