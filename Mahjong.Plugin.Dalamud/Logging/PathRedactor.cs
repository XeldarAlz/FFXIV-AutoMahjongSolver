using System;

namespace Mahjong.Plugin.Dalamud.Logging;

internal static class PathRedactor
{
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var normalised = path.Replace('/', '\\');

        foreach (var marker in WellKnownLauncherRoots)
        {
            int idx = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return normalised.Substring(idx + 1);
        }

        foreach (var marker in GenericRoots)
        {
            int idx = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return normalised.Substring(idx + 1);
        }

        var parts = normalised.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return parts[^2] + "\\" + parts[^1];
        return parts.Length == 1 ? parts[0] : "(redacted)";
    }

    // Leading "\" is part of the marker to avoid matching mid-segment substrings (e.g. a directory literally named "Roaming").
    private static readonly string[] WellKnownLauncherRoots =
    {
        "\\XIVLauncher\\",
        "\\XIVLauncherCN\\",
        "\\.xlcore\\",
    };

    private static readonly string[] GenericRoots =
    {
        "\\Roaming\\",
        "\\Documents\\",
        "\\AppData\\",
    };
}
