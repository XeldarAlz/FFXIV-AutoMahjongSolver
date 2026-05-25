using System;
using System.IO;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

internal static class TestPaths
{
    private const string RepoRootMarker = "Mahjong.Plugin.Dalamud.sln";

    public static string RepoRoot { get; } = ResolveRepoRoot();

    public static string LayoutsDir => Path.Combine(RepoRoot, "data", "layouts");

    public static string FixturesDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "Replay", "fixtures");

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, RepoRootMarker)))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException(
            $"repo root marker '{RepoRootMarker}' not found above {AppContext.BaseDirectory}");
    }
}
