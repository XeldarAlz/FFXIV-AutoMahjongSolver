namespace Mahjong.Core.Tests;

/// <summary>
/// Every record type in Mahjong.Core that takes a list-typed input must
/// defensive-copy at construction so the snapshot can't be corrupted by a
/// caller who keeps mutating their buffer. These tests pin that contract.
/// </summary>
public class DefensiveCopyTests
{
    [Fact]
    public void SeatView_copies_discards_melds_and_tedashi_flags()
    {
        var discards = new List<Tile> { Tile.FromId(0) };
        var tedashi = new List<bool> { true };
        var melds = new List<Meld> { Meld.AnKan(Tile.FromId(1)) };

        var view = new SeatView(
            Discards: discards,
            DiscardIsTedashi: tedashi,
            Melds: melds,
            Riichi: false,
            RiichiDiscardIndex: -1,
            Ippatsu: false,
            IsTenpaiCalled: false);

        discards.Clear();
        tedashi.Clear();
        melds.Clear();

        Assert.Single(view.Discards);
        Assert.Single(view.DiscardIsTedashi);
        Assert.Single(view.Melds);
    }

    [Fact]
    public void StateSnapshot_copies_every_list_input()
    {
        var hand = new List<Tile> { Tile.FromId(0) };
        var melds = new List<Meld> { Meld.AnKan(Tile.FromId(0)) };
        var scores = new List<int> { 25000, 25000, 25000, 25000 };
        var dora = new List<Tile> { Tile.FromId(2) };
        var ura = new List<Tile> { Tile.FromId(3) };
        var seats = new List<SeatView> { EmptySeat(), EmptySeat(), EmptySeat(), EmptySeat() };

        var snap = new StateSnapshot(
            Hand: hand,
            OurMelds: melds,
            OurSeat: 0,
            OurRiichi: false,
            OurIppatsu: false,
            OurDoubleRiichi: false,
            RoundWind: 0,
            Honba: 0,
            RiichiSticks: 0,
            Scores: scores,
            DoraIndicators: dora,
            UraDoraIndicators: ura,
            WallRemaining: 70,
            TurnIndex: 0,
            DealerSeat: 0,
            Seats: seats,
            Legal: LegalActions.None,
            SchemaVersion: StateSnapshot.CurrentSchemaVersion);

        hand.Clear();
        melds.Clear();
        scores.Clear();
        dora.Clear();
        ura.Clear();
        seats.Clear();

        Assert.Single(snap.Hand);
        Assert.Single(snap.OurMelds);
        Assert.Equal(4, snap.Scores.Count);
        Assert.Single(snap.DoraIndicators);
        Assert.Single(snap.UraDoraIndicators);
        Assert.Equal(4, snap.Seats.Count);
    }

    [Fact]
    public void LegalActions_copies_every_candidate_list()
    {
        var discards = new List<Tile> { Tile.FromId(0) };
        var pons = new List<MeldCandidate> { new(MeldKind.Pon, Tile.FromId(0), [], 1) };
        var chis = new List<MeldCandidate> { new(MeldKind.Chi, Tile.FromId(0), [], 3) };
        var kans = new List<MeldCandidate>();

        var legal = new LegalActions(ActionFlags.Discard | ActionFlags.Pon | ActionFlags.Chi,
            discards, pons, chis, kans);

        discards.Clear();
        pons.Clear();
        chis.Clear();

        Assert.Single(legal.DiscardableTiles);
        Assert.Single(legal.PonCandidates);
        Assert.Single(legal.ChiCandidates);
        Assert.Empty(legal.KanCandidates);
    }

    [Fact]
    public void WinContext_copies_dora_indicator_lists()
    {
        var dora = new List<Tile> { Tile.FromId(0) };
        var ura = new List<Tile> { Tile.FromId(1) };

        var ctx = new WinContext(
            WinningTile: Tile.FromId(5),
            Kind: WinKind.Tsumo,
            DoraIndicators: dora,
            UraDoraIndicators: ura);

        dora.Clear();
        ura.Clear();

        Assert.Single(ctx.Dora);
        Assert.Single(ctx.UraDora);
    }

    [Fact]
    public void WinContext_with_null_indicators_yields_empty_lists()
    {
        var ctx = new WinContext(Tile.FromId(0), WinKind.Tsumo);

        Assert.Empty(ctx.Dora);
        Assert.Empty(ctx.UraDora);
    }

    [Fact]
    public void Decomposition_copies_groups_list()
    {
        var groups = new List<Group>
        {
            new(GroupKind.Pair, Tile.FromId(0), IsOpen: false),
            new(GroupKind.Triplet, Tile.FromId(2), IsOpen: false),
        };

        var d = new Decomposition(
            Form: DecompositionForm.Standard,
            Groups: groups,
            IsMenzen: true,
            WinningTile: Tile.FromId(0),
            WinningTileFromOpponent: false);

        groups.Clear();

        Assert.Equal(2, d.Groups.Count);
    }

    [Fact]
    public void ScoreResult_copies_yaku_list()
    {
        var yaku = new List<YakuHit> { new(Yaku.Tanyao, 1) };
        var d = new Decomposition(DecompositionForm.Standard, [], true, Tile.FromId(0), false);

        var result = new ScoreResult(d, yaku, Han: 1, Fu: 30, BasePoints: 100,
            Payments: default, TierName: "");

        yaku.Clear();

        Assert.Single(result.Yaku);
    }

    private static SeatView EmptySeat() => new(
        Discards: [],
        DiscardIsTedashi: [],
        Melds: [],
        Riichi: false,
        RiichiDiscardIndex: -1,
        Ippatsu: false,
        IsTenpaiCalled: false);
}
