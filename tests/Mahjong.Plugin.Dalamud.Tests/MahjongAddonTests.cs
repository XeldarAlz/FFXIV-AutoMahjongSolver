using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Tests;

public class MahjongAddonTests
{
    [Theory]
    [InlineData("Emj")]
    [InlineData("EmjL")]
    public void IsMahjongAddon_recognizes_known_names(string name)
    {
        Assert.True(MahjongAddon.IsMahjongAddon(name));
    }

    [Theory]
    [InlineData("emj")]
    [InlineData("EMJ")]
    [InlineData("EmjAuth")]
    [InlineData("LobbyChat")]
    [InlineData("")]
    public void IsMahjongAddon_rejects_unknown_or_mismatched_names(string name)
    {
        Assert.False(MahjongAddon.IsMahjongAddon(name));
    }

    [Fact]
    public void CandidateNames_includes_emj_first_then_emjL()
    {
        // Order pinned because TryGet iterates this list; Emj is the common match.
        var names = MahjongAddon.CandidateNames;
        Assert.Equal("Emj", names[0]);
        Assert.Equal("EmjL", names[1]);
        Assert.Equal(2, names.Count);
    }
}
