using Mahjong.Plugin.Dalamud.Logging;

namespace Mahjong.Plugin.Dalamud.Tests;

public class PathRedactorTests
{
    [Theory]
    [InlineData(@"C:\Users\xelda\AppData\Roaming\XIVLauncher\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts",
                @"XIVLauncher\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts")]
    [InlineData(@"C:\Users\ベネ\AppData\Roaming\XIVLauncher\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts",
                @"XIVLauncher\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts")]
    [InlineData(@"C:\Users\14358\AppData\Roaming\XIVLauncherCN\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts",
                @"XIVLauncherCN\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts")]
    [InlineData(@"Z:\home\lux\.xlcore\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts",
                @".xlcore\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts")]
    public void Strips_username_for_well_known_launcher_roots(string input, string expected)
    {
        Assert.Equal(expected, PathRedactor.Redact(input));
    }

    [Theory]
    [InlineData(@"/home/lux/.xlcore/installedPlugins/Mahjong.Plugin.Dalamud/0.1.0.0/layouts",
                @".xlcore\installedPlugins\Mahjong.Plugin.Dalamud\0.1.0.0\layouts")]
    public void Normalises_unix_separators(string input, string expected)
    {
        Assert.Equal(expected, PathRedactor.Redact(input));
    }

    [Fact]
    public void Falls_back_to_trailing_two_segments_for_dev_clones()
    {
        var redacted = PathRedactor.Redact(
            @"C:\Users\kamot\Documents\GitHub\FFXIV-DomanMahjongSolver\Mahjong.Plugin.Dalamud\bin\x64\Debug\layouts");
        Assert.Equal(
            @"Documents\GitHub\FFXIV-DomanMahjongSolver\Mahjong.Plugin.Dalamud\bin\x64\Debug\layouts",
            redacted);
    }

    [Fact]
    public void Trims_to_two_segments_when_no_marker_present()
    {
        var redacted = PathRedactor.Redact(@"D:\custom\path\layouts");
        Assert.Equal(@"path\layouts", redacted);
    }

    [Fact]
    public void Empty_or_null_input_returns_empty()
    {
        Assert.Equal(string.Empty, PathRedactor.Redact(null));
        Assert.Equal(string.Empty, PathRedactor.Redact(""));
    }

    [Fact]
    public void Single_segment_relative_path_passes_through()
    {
        Assert.Equal("layouts", PathRedactor.Redact("layouts"));
    }
}
