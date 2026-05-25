using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tests;

public class HeuristicCallPolicyTests
{
    private static readonly HeuristicCallPolicy Policy = new();

    private static StateSnapshot WithLegal(StateSnapshot s, LegalActions legal) =>
        s with { Legal = legal };

    private static StateSnapshot ClosedHandWith(string notation)
        => Snapshots.Closed14(notation, ActionFlags.Discard);

    [Fact]
    public void Declines_when_no_candidates_offered()
    {
        var s = ClosedHandWith("123m456p789s234s55m");
        var legal = new LegalActions(ActionFlags.Pass, [], [], [], []);
        var d = Policy.Evaluate(WithLegal(s, legal));
        Assert.False(d.Accept);
        Assert.Equal("no-candidate", d.Reason.Code);
    }

    [Fact]
    public void Accepts_pon_that_advances_shanten_with_yakuhai()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("234m456p789s234s11z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Pon | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [new MeldCandidate(MeldKind.Pon, Tile.FromId(31), [Tile.FromId(31), Tile.FromId(31)], FromSeat: 1)],
                ChiCandidates: [],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.NotNull(d.Reason);
        if (d.Accept)
        {
            Assert.NotNull(d.Value);
            Assert.Equal(MeldKind.Pon, d.Value!.Value.Kind);
        }
    }

    [Fact]
    public void Declines_when_no_call_advances_shanten()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("234m456p789s234s5z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(0),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.False(d.Accept);
        Assert.Equal("no-shanten-gain-with-yaku", d.Reason.Code);
    }

    [Fact]
    public void Accepts_chi_that_unlocks_sanshoku_doujun()
    {
        // chi(234m claiming 4m) + closed 234p + 234s → post-call sanshoku-doujun at
        // offset 1 is reachable across all three suits. No other yaku route fires
        // (terminals/honors block tanyao, multi-suit blocks honitsu, chi blocks toitoi,
        // no dragons/seat-winds for yakuhai). Pre-fix the call would be declined.
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("23m234p234s1p9s1z2z4z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(3),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.True(d.Accept,
            $"expected chi(234m) accepted via sanshoku-doujun (reason={d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Doman_declines_chi_with_lone_yakuhai_pair_below_min_han()
    {
        // Reproduces issue #50: post-Chi shape (7m 44p 789p 345s 55z + Chi 789p meld) has
        // only 1 reachable han via the 55z dragon pair. Doman MinHan=2 must reject.
        var policy = new HeuristicCallPolicy(new DomanRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("7m4478899p345s55z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(15),
                    [Tile.FromId(16), Tile.FromId(17)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.False(d.Accept,
            $"Doman should decline lone-yakuhai-pair Chi (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal("no-shanten-gain-with-yaku", d.Reason.Code);
    }

    [Fact]
    public void Riichi_accepts_same_chi_at_min_han_one()
    {
        // Same hand as the Doman test; Riichi MinHan=1, 1-han yakuhai suffices.
        var policy = new HeuristicCallPolicy(new RiichiRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("7m4478899p345s55z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(15),
                    [Tile.FromId(16), Tile.FromId(17)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.True(d.Accept,
            $"Riichi should accept this Chi via yakuhai (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Doman_accepts_chi_when_two_yakuhai_pairs_total_two_han()
    {
        // Two dragon pairs (55z + 66z) reach 2 han, satisfying Doman MinHan=2.
        var policy = new HeuristicCallPolicy(new DomanRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("24777m189p9s5566z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(2),
                    [Tile.FromId(1), Tile.FromId(3)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.True(d.Accept,
            $"Doman should accept Chi with two dragon pairs reaching 2 han (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Accepts_chi_that_unlocks_ittsu()
    {
        // chi(123m claiming 1m) + closed 456m + 789m → post-call ittsu in m-suit is
        // reachable: subrun 0 chi-locked, subruns 1 and 2 fully present in closed.
        // No other yaku route fires (chi-meld terminal blocks tanyao, multi-suit
        // closed blocks honitsu, chi blocks toitoi, sanshoku needs all-three-suits
        // and p/s have only singletons at offset 0).
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("23m456m789m1p9s1z2z4z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(0),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.True(d.Accept,
            $"expected chi(123m) accepted via ittsu (reason={d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }
}
