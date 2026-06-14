using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class DiscardEvaluatorTests
{
    private static List<ScoredDiscard> Rank(string notation, EvalContext ctx, IReadOnlyList<Meld>? melds = null)
        => DiscardEvaluator.Rank(Tiles.ParseCounts(notation), melds, seen: null, ctx);

    private static ScoredDiscard Cut(List<ScoredDiscard> ranked, string tile)
        => ranked.First(r => r.Option.Tile == Tiles.Index(tile));

    [Fact]
    public void ShantenStillDominatesValue()
    {
        var ranked = Rank("23455m234p234567s", new EvalContext());
        Assert.Equal(0, ranked[0].Option.Shanten);
        for (int i = 1; i < ranked.Count; i++)
            Assert.True(ranked[i - 1].Option.Shanten <= ranked[i].Option.Shanten);
    }

    [Fact]
    public void TenpaiValue_CountsDoraHeldInTheWinningHand()
    {
        // Indicator 1m makes 2m dora. Cutting 5m keeps the 2m (dora in every win, twice on a 2m
        // win); cutting 2m only sees dora when the 2m itself is won, so the dora-keeping cut gains
        // more from the indicator. (Cutting 2m can still rank higher overall — its 2m win carries
        // pinfu + sanshoku — which is exactly the point of valuing whole hands, not lone tiles.)
        var plain = new EvalContext();
        var dora = new EvalContext { DoraIndicators = new[] { Tiles.Index("1m") } };

        long cut5mPlain = Cut(Rank("23455m234p234567s", plain), "5m").Value;
        long cut5mDora = Cut(Rank("23455m234p234567s", dora), "5m").Value;
        long cut2mPlain = Cut(Rank("23455m234p234567s", plain), "2m").Value;
        long cut2mDora = Cut(Rank("23455m234p234567s", dora), "2m").Value;

        Assert.True(cut5mDora > cut5mPlain);
        Assert.True(cut5mDora - cut5mPlain > cut2mDora - cut2mPlain);
    }

    [Fact]
    public void OpenYakulessWait_IsWorthZero_ClosedSameShapeIsNot()
    {
        // 23455m 234p 567s + open chii 789m: every tenpai cut leaves a wait with no yaku (the 9m
        // kills tanyao), which can never win — value 0. Fully concealed, riichi makes it live.
        var openMelds = new[]
        {
            new Meld(MeldKind.Chii, new[] { Tiles.Index("7m"), Tiles.Index("8m"), Tiles.Index("9m") }),
        };
        var openRanked = Rank("23455m234p567s", new EvalContext { Closed = false }, openMelds);
        Assert.Equal(0, openRanked.First(r => r.Option.Shanten == 0).Value);

        var closedRanked = Rank("23455789m234p567s", new EvalContext());
        Assert.True(closedRanked.First(r => r.Option.Shanten == 0).Value > 0);
    }

    [Fact]
    public void DealerWinsAreWorthMore()
    {
        long nonDealer = Rank("23455m234p234567s", new EvalContext()).First(r => r.Option.Shanten == 0).Value;
        long dealer = Rank("23455m234p234567s", new EvalContext { Dealer = true }).First(r => r.Option.Shanten == 0).Value;
        Assert.True(dealer > nonDealer);
    }

    [Fact]
    public void Progress_AddsHeldDoraToNonTenpaiCuts()
    {
        // Cutting one 4z stays 1-shanten and keeps the other; with indicator 3z that kept 4z is
        // a dora worth one DoraWeight of held value.
        var plain = new EvalContext();
        var dora = new EvalContext { DoraIndicators = new[] { Tiles.Index("3z") } };

        ScoredDiscard cut4zPlain = Cut(Rank("234m5678p23488s44z", plain), "4z");
        ScoredDiscard cut4zDora = Cut(Rank("234m5678p23488s44z", dora), "4z");

        Assert.Equal(1, cut4zPlain.Option.Shanten);
        Assert.Equal(cut4zPlain.Value + 40, cut4zDora.Value);
    }

    [Fact]
    public void AkaIsOnlyLostWhenNoPlainCopyRemains()
    {
        // Single red 5p in hand: cutting the 5p loses the aka (no value change vs no-aka context);
        // cutting anything else keeps it (+1 aka of held value at 1-shanten).
        var aka = new int[Tiles.TypeCount];
        aka[Tiles.Index("5p")] = 1;
        var withAka = new EvalContext { AkaByType = aka };
        var without = new EvalContext();

        const string hand = "234m5678p23488s44z";
        long cut5pAka = Cut(Rank(hand, withAka), "5p").Value;
        long cut5pPlain = Cut(Rank(hand, without), "5p").Value;
        long cut4zAka = Cut(Rank(hand, withAka), "4z").Value;
        long cut4zPlain = Cut(Rank(hand, without), "4z").Value;

        Assert.Equal(cut5pPlain, cut5pAka);          // aka went with the discard
        Assert.Equal(cut4zPlain + 40, cut4zAka);     // aka kept: one dora of held value
    }
}
