using System;

namespace Mahjong.Plugin.Dalamud.Logging;

/// <summary>
/// Strips user-identifying prefixes from filesystem paths before they enter
/// the telemetry corpus. Telemetry uploads are anonymous (one GUID per
/// install), so a Windows/Linux path containing a username is the only
/// realistic PII leak vector left — the corpus from 2026-05 collected nine
/// distinct usernames before this scrubbing was added.
///
/// <para>The redactor recognises the standard XIVLauncher install layouts on
/// Windows (<c>%AppData%\XIVLauncher</c>), Wine/<c>xlcore</c> on Linux/macOS
/// (<c>~/.xlcore</c>), and the Chinese launcher (<c>%AppData%\XIVLauncherCN</c>).
/// Anything outside those well-known shapes — debug builds, local clones —
/// is collapsed to its trailing two segments so the layout name still
/// surfaces without the username.</para>
/// </summary>
internal static class PathRedactor
{
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // Normalise separators so a single set of regex/IndexOf calls handles
        // both Windows ("\") and Wine-rewrite ("/") forms.
        var normalised = path.Replace('/', '\\');

        // Well-known launcher roots — anything before the launcher root is the
        // username and must go.
        foreach (var marker in WellKnownLauncherRoots)
        {
            int idx = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return normalised.Substring(idx + 1); // drop the leading "\"
        }

        // Generic Windows AppData / Documents fallback. Catches non-XIVLauncher
        // installs and side-loaded debug builds: only the trailing relative
        // path past the last well-known token is preserved.
        foreach (var marker in GenericRoots)
        {
            int idx = normalised.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return normalised.Substring(idx + 1);
        }

        // Otherwise collapse to the last two path segments — "<parent>\<leaf>"
        // is enough for analyzers to spot the layout dir without the
        // surrounding user namespace.
        var parts = normalised.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return parts[^2] + "\\" + parts[^1];
        return parts.Length == 1 ? parts[0] : "(redacted)";
    }

    // The leading "\" is part of the marker so we don't accidentally match
    // mid-segment substrings (e.g. a directory literally named "Roaming").
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
