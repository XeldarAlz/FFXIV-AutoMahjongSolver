using System.IO;

namespace Mahjong.Replay;

public static class RepoPathResolver
{
    private const string MarkerFile = "Mahjong.Plugin.Dalamud.sln";

    public static string Root() => TryFindRoot()
        ?? throw new DirectoryNotFoundException(
            $"Could not locate '{MarkerFile}' walking up from {AppContext.BaseDirectory}");

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
