using System.IO;

namespace Mahjong.Replay;

/// <summary>
/// Locates the repository root by walking up from the test assembly's
/// directory looking for a marker file (the solution file). Used by the
/// golden-file harness so test fixtures and snapshots live in
/// <c>data/replays/</c> at the repo root — committable and visible in
/// <c>git status</c> — instead of being copied into per-test bin folders.
/// </summary>
public static class RepoPathResolver
{
    private const string MarkerFile = "Mahjong.Plugin.Dalamud.sln";

    /// <summary>Find the repo root or throw if no <c>.sln</c> ancestor exists.</summary>
    public static string Root() => TryFindRoot()
        ?? throw new DirectoryNotFoundException(
            $"Could not locate '{MarkerFile}' walking up from {AppContext.BaseDirectory}");

    /// <summary>Resolve a path relative to the repo root.</summary>
    public static string Resolve(params string[] relativeSegments)
    {
        ArgumentNullException.ThrowIfNull(relativeSegments);
        var segments = new string[relativeSegments.Length + 1];
        segments[0] = Root();
        Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);
        return Path.Combine(segments);
    }

    private static string? TryFindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, MarkerFile)))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
