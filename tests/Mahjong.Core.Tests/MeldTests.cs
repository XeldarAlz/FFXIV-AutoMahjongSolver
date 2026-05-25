namespace Mahjong.Core.Tests;

public class MeldTests
{
    [Fact]
    public void Chi_factory_lays_out_consecutive_run()
    {
        var meld = Meld.Chi(Tile.FromId(2), Tile.FromId(3), fromSeat: 1);

        Assert.Equal(MeldKind.Chi, meld.Kind);
        Assert.Equal([2, 3, 4], meld.Tiles.Select(t => (int)t.Id));
        Assert.Equal(Tile.FromId(3), meld.ClaimedTile);
        Assert.Equal(1, meld.ClaimedFromSeat);
        Assert.True(meld.IsOpen);
        Assert.False(meld.IsKan);
        Assert.Equal(3, meld.TileCount);
    }

    [Fact]
    public void Chi_factory_rejects_honor_anchor()
    {
        Assert.Throws<ArgumentException>(() => Meld.Chi(Tile.FromId(27), Tile.FromId(27), 0));
    }

    [Fact]
    public void Pon_factory_produces_three_of_a_kind()
    {
        var meld = Meld.Pon(Tile.FromId(31), Tile.FromId(31), fromSeat: 2);

        Assert.Equal(MeldKind.Pon, meld.Kind);
        Assert.All(meld.Tiles, t => Assert.Equal(31, t.Id));
        Assert.Equal(2, meld.ClaimedFromSeat);
    }

    [Fact]
    public void AnKan_is_concealed_and_has_four_tiles()
    {
        var meld = Meld.AnKan(Tile.FromId(0));

        Assert.Equal(MeldKind.AnKan, meld.Kind);
        Assert.Equal(4, meld.TileCount);
        Assert.False(meld.IsOpen);
        Assert.True(meld.IsKan);
        Assert.Null(meld.ClaimedTile);
        Assert.Equal(-1, meld.ClaimedFromSeat);
    }

    [Theory]
    [InlineData(MeldKind.MinKan)]
    [InlineData(MeldKind.ShouMinKan)]
    public void Open_kans_are_open(MeldKind kind)
    {
        var meld = kind == MeldKind.MinKan
            ? Meld.MinKan(Tile.FromId(0), Tile.FromId(0), 1)
            : Meld.ShouMinKan(Tile.FromId(0), Tile.FromId(0), 1);

        Assert.True(meld.IsOpen);
        Assert.True(meld.IsKan);
    }

    [Fact]
    public void FromAcceptedCandidate_for_chi_anchors_to_lowest_tile()
    {
        var candidate = new MeldCandidate(
            Kind: MeldKind.Chi,
            ClaimedTile: Tile.FromId(4),
            HandTiles: [Tile.FromId(2), Tile.FromId(3)],
            FromSeat: 3);

        var meld = Meld.FromAcceptedCandidate(candidate);

        Assert.Equal([2, 3, 4], meld.Tiles.Select(t => (int)t.Id));
    }

    [Fact]
    public void FromAcceptedCandidate_rejects_ankan()
    {
        var candidate = new MeldCandidate(MeldKind.AnKan, Tile.FromId(0), [], -1);
        Assert.Throws<ArgumentException>(() => Meld.FromAcceptedCandidate(candidate));
    }
}
