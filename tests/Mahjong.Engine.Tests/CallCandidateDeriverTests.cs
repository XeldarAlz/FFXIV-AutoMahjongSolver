using System.Linq;
using Xunit;

namespace Mahjong.Engine.Tests;

public class CallCandidateDeriverTests
{
    private static Tile T(string notation) => Tiles.Parse(notation).Single();

    private const int Kamicha = 3;
    private const int Shimocha = 1;

    [Fact]
    public void Pon_when_two_copies_in_hand()
    {
        var hand = Tiles.Parse("55m123p456s11z77z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("5m"), Shimocha);

        Assert.Single(r.Pon);
        var c = r.Pon[0];
        Assert.Equal(MeldKind.Pon, c.Kind);
        Assert.Equal(T("5m"), c.ClaimedTile);
        Assert.Equal(2, c.HandTiles.Length);
        Assert.All(c.HandTiles, t => Assert.Equal(T("5m"), t));
        Assert.Equal(Shimocha, c.FromSeat);

        Assert.Empty(r.Kan);
        Assert.Empty(r.Chi);
    }

    [Fact]
    public void Pon_and_minkan_when_three_copies_in_hand()
    {
        var hand = Tiles.Parse("555m123p456s11z7z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("5m"), Shimocha);

        Assert.Single(r.Pon);
        Assert.Single(r.Kan);
        Assert.Equal(MeldKind.MinKan, r.Kan[0].Kind);
        Assert.Equal(3, r.Kan[0].HandTiles.Length);
    }

    [Fact]
    public void No_pon_with_single_copy()
    {
        var hand = Tiles.Parse("5m123p456s11z777z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("5m"), Shimocha);
        Assert.Empty(r.Pon);
        Assert.Empty(r.Kan);
    }

    [Fact]
    public void Chi_low_end_claimed_as_sequence_start()
    {
        var hand = Tiles.Parse("34m123p34s11z77z9m").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("2s"), Kamicha);

        Assert.Single(r.Chi);
        var c = r.Chi[0];
        Assert.Equal(MeldKind.Chi, c.Kind);
        var ids = c.HandTiles.Select(t => t.Id).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { T("3s").Id, T("4s").Id }, ids);
    }

    [Fact]
    public void Chi_middle_claimed_multiple_variants()
    {
        var hand = Tiles.Parse("3467s11z123m456p8p").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("5s"), Kamicha);

        Assert.Equal(3, r.Chi.Count);
        var sets = r.Chi.Select(c => string.Join(",", c.HandTiles.Select(t => t.Id).OrderBy(x => x))).ToHashSet();
        Assert.Contains($"{T("3s").Id},{T("4s").Id}", sets);
        Assert.Contains($"{T("4s").Id},{T("6s").Id}", sets);
        Assert.Contains($"{T("6s").Id},{T("7s").Id}", sets);
    }

    [Fact]
    public void Chi_not_offered_from_non_kamicha()
    {
        var hand = Tiles.Parse("34m123p34s11z77z9m").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("2s"), Shimocha);
        Assert.Empty(r.Chi);
    }

    [Fact]
    public void Chi_not_offered_across_suit_boundary()
    {
        var hand = Tiles.Parse("9m1p2p123s456s77z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("1m"), Kamicha);

        Assert.Empty(r.Chi);
    }

    [Fact]
    public void Chi_not_offered_for_honors()
    {
        var hand = Tiles.Parse("1122334m567p11z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("1z"), Kamicha);
        Assert.Empty(r.Chi);
    }

    [Fact]
    public void Chi_edge_1_only_gives_high_variant()
    {
        var hand = Tiles.Parse("23m456p789s1122z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("1m"), Kamicha);
        Assert.Single(r.Chi);
    }

    [Fact]
    public void Chi_edge_9_only_gives_low_variant()
    {
        var hand = Tiles.Parse("78m456p789s1122z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("9m"), Kamicha);
        Assert.Single(r.Chi);
    }

    [Fact]
    public void No_candidates_when_claimed_tile_absent_and_no_runs()
    {
        var hand = Tiles.Parse("17m258p369s1234z").ToList();
        var r = CallCandidateDeriver.Derive(hand, T("5z"), Kamicha);
        Assert.Empty(r.Pon);
        Assert.Empty(r.Chi);
        Assert.Empty(r.Kan);
    }
}
