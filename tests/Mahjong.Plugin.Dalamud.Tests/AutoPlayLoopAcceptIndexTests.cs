using Mahjong.Core;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Policy.Abstractions;

namespace Mahjong.Plugin.Dalamud.Tests;

public class AutoPlayLoopAcceptIndexTests
{
    private static MeldCandidate MakePon(int tileId, int fromSeat = 1) =>
        new(MeldKind.Pon, Tile.FromId(tileId), [Tile.FromId(tileId), Tile.FromId(tileId)], fromSeat);

    private static MeldCandidate MakeChi(int claimedId, int low, int high, int fromSeat = 3)
    {
        var handTiles = new List<Tile>();
        for (int id = low; id <= high; id++)
            if (id != claimedId)
                handTiles.Add(Tile.FromId(id));
        return new MeldCandidate(MeldKind.Chi, Tile.FromId(claimedId), handTiles.ToArray(), fromSeat);
    }

    private static MeldCandidate MakeKan(int tileId, int fromSeat = 1) =>
        new(MeldKind.MinKan, Tile.FromId(tileId),
            [Tile.FromId(tileId), Tile.FromId(tileId), Tile.FromId(tileId)], fromSeat);

    [Fact]
    public void Pon_alone_returns_index_0()
    {
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Pass,
            [], [MakePon(5)], [], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Pon, legal, MakePon(5));
        Assert.Equal(0, idx);
    }

    [Fact]
    public void Pon_and_chi_simultaneous_chi_picks_index_after_pon()
    {
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Chi | ActionFlags.Pass,
            [], [MakePon(5)], [MakeChi(3, 2, 4)], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Chi, legal, MakeChi(3, 2, 4));
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Pon_and_chi_simultaneous_pon_still_picks_index_0()
    {
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Chi | ActionFlags.Pass,
            [], [MakePon(5)], [MakeChi(3, 2, 4)], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Pon, legal, MakePon(5));
        Assert.Equal(0, idx);
    }

    [Fact]
    public void Multi_chi_first_variant_picks_index_0()
    {
        var chi123 = MakeChi(claimedId: 1, low: 0, high: 2);
        var chi234 = MakeChi(claimedId: 1, low: 1, high: 3);
        var legal = new LegalActions(
            ActionFlags.Chi | ActionFlags.Pass,
            [], [], [chi123, chi234], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Chi, legal, chi123);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void Multi_chi_second_variant_picks_index_1()
    {
        var chi123 = MakeChi(claimedId: 1, low: 0, high: 2);
        var chi234 = MakeChi(claimedId: 1, low: 1, high: 3);
        var legal = new LegalActions(
            ActionFlags.Chi | ActionFlags.Pass,
            [], [], [chi123, chi234], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Chi, legal, chi234);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Multi_chi_with_pon_third_variant_picks_index_3()
    {
        var chi0 = MakeChi(claimedId: 1, low: 0, high: 2);
        var chi1 = MakeChi(claimedId: 1, low: 1, high: 3);
        var chi2 = MakeChi(claimedId: 1, low: 2, high: 4);
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Chi | ActionFlags.Pass,
            [], [MakePon(5)], [chi0, chi1, chi2], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Chi, legal, chi2);
        Assert.Equal(3, idx);
    }

    [Fact]
    public void Pon_and_kan_simultaneous_kan_picks_index_1()
    {
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.MinKan | ActionFlags.Pass,
            [], [MakePon(5)], [], [MakeKan(5)]);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.MinKan, legal, MakeKan(5));
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Ron_picks_first_index_when_only_action()
    {
        var legal = new LegalActions(
            ActionFlags.Ron | ActionFlags.Pass,
            [], [], [], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Ron, legal, null);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void Riichi_with_chi_in_prompt_picks_index_after_chi_slots()
    {
        var chi = MakeChi(claimedId: 1, low: 0, high: 2);
        var legal = new LegalActions(
            ActionFlags.Chi | ActionFlags.Riichi | ActionFlags.Pass,
            [], [], [chi], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Riichi, legal, null);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Tsumo_with_riichi_in_prompt_picks_last_accept_index()
    {
        var legal = new LegalActions(
            ActionFlags.Riichi | ActionFlags.Tsumo | ActionFlags.Pass,
            [], [], [], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Tsumo, legal, null);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Chi_with_no_matching_candidate_falls_back_to_first_chi_slot()
    {
        var chi1 = MakeChi(claimedId: 1, low: 0, high: 2);
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Chi | ActionFlags.Pass,
            [], [MakePon(5)], [chi1], []);

        var ghostChi = MakeChi(claimedId: 8, low: 7, high: 9);
        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Chi, legal, ghostChi);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void Ron_with_pon_offered_picks_index_after_pon()
    {
        var legal = new LegalActions(
            ActionFlags.Pon | ActionFlags.Ron | ActionFlags.Pass,
            [], [MakePon(5)], [], []);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Ron, legal, null);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void AnKan_alone_picks_index_0()
    {
        var legal = new LegalActions(
            ActionFlags.AnKan | ActionFlags.Discard,
            [], [], [], [MakeKan(5)]);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.AnKan, legal, null);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void AnKan_with_riichi_and_tsumo_picks_first_kan_slot()
    {
        var legal = new LegalActions(
            ActionFlags.AnKan | ActionFlags.Riichi | ActionFlags.Tsumo | ActionFlags.Discard,
            [], [], [], [MakeKan(5)]);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.AnKan, legal, null);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void Riichi_at_self_declare_with_ankan_picks_index_after_ankan()
    {
        var legal = new LegalActions(
            ActionFlags.AnKan | ActionFlags.Riichi | ActionFlags.Tsumo | ActionFlags.Discard,
            [], [], [], [MakeKan(5)]);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.Riichi, legal, null);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void ShouMinKan_with_ankan_offered_picks_index_after_ankan()
    {
        var legal = new LegalActions(
            ActionFlags.AnKan | ActionFlags.ShouMinKan | ActionFlags.Discard,
            [], [], [], [MakeKan(5)]);

        int idx = AutoPlayLoop.ComputeAcceptIndex(ActionKind.ShouMinKan, legal, null);
        Assert.Equal(1, idx);
    }
}
