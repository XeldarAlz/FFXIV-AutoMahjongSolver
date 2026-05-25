namespace Mahjong.Replay.Tests;

public class TenhouLogTests
{
    [Fact]
    public void From136_maps_each_quad_to_same_34_id()
    {
        Assert.Equal(0, TenhouLog.From136(0).Id);
        Assert.Equal(0, TenhouLog.From136(3).Id);

        Assert.Equal(9, TenhouLog.From136(36).Id);
        Assert.Equal(9, TenhouLog.From136(39).Id);

        Assert.Equal(18, TenhouLog.From136(72).Id);
        Assert.Equal(27, TenhouLog.From136(108).Id);
        Assert.Equal(33, TenhouLog.From136(135).Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(136)]
    public void From136_rejects_out_of_range(int pai)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TenhouLog.From136(pai));
    }

    [Fact]
    public void IsRed5_detects_aka_dora_slots()
    {
        Assert.True(TenhouLog.IsRed5(TenhouLog.RedFiveMan));
        Assert.True(TenhouLog.IsRed5(TenhouLog.RedFivePin));
        Assert.True(TenhouLog.IsRed5(TenhouLog.RedFiveSou));
        Assert.False(TenhouLog.IsRed5(17));
    }

    [Theory]
    [InlineData("r60", TenhouLog.EventKind.Riichi, 15)]
    [InlineData("c12", TenhouLog.EventKind.Chi, 3)]
    [InlineData("p44", TenhouLog.EventKind.Pon, 11)]
    [InlineData("k108", TenhouLog.EventKind.Kan, 27)]
    public void ParseEventTag_extracts_kind_and_tile_id(string tag, TenhouLog.EventKind kind, int expectedId)
    {
        var (k, id) = TenhouLog.ParseEventTag(tag);
        Assert.Equal(kind, k);
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void ParseEventTag_returns_negative_id_for_empty_tag()
    {
        var (_, id) = TenhouLog.ParseEventTag(string.Empty);
        Assert.Equal(-1, id);
    }

    [Fact]
    public void ParseKyoku_reads_starting_state_from_minimal_log()
    {
        const string json = """
{
  "log": [
    [
      [0, 0, 0],
      [25000, 25000, 25000, 25000],
      [16],
      [],
      [0,1,2,3,4,5,6,7,8,9,10,11,12],
      [],
      [],
      [36,37,38,39,40,41,42,43,44,45,46,47,48],
      [],
      [],
      [72,73,74,75,76,77,78,79,80,81,82,83,84],
      [],
      [],
      [108,109,110,111,112,113,114,115,116,117,118,119,120],
      [],
      []
    ]
  ]
}
""";
        var kyokus = TenhouLog.ParseDocument(json);
        Assert.Single(kyokus);

        var k = kyokus[0];
        Assert.Equal(0, k.Round);
        Assert.Equal(0, k.Dealer);
        Assert.Equal(0, k.Honba);
        Assert.Equal([25000, 25000, 25000, 25000], k.StartScores);
        Assert.Single(k.DoraIndicators);
        Assert.Equal(4, k.StartingHands.Length);
        Assert.Equal(13, k.StartingHands[0].Length);

        Assert.Equal(0, k.StartingHands[0][0].Id);
        Assert.Equal(3, k.StartingHands[0][12].Id);
    }

    [Fact]
    public void ParseDocument_rejects_json_without_log_field()
    {
        Assert.Throws<ArgumentException>(() => TenhouLog.ParseDocument("""{"foo": 1}"""));
    }
}
