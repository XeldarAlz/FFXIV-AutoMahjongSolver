namespace AutoMahjongSolver.Core.Game;

/// <summary>
/// Localized Emj panel-string matching. Only strings confirmed on a live client are listed here;
/// other client languages are deliberately absent rather than guessed — run <c>/amjs state</c> on
/// the target client to capture the real seat-wind / riichi strings, then add them as verified
/// entries. Matched winds normalize to the canonical English word the rest of the code expects.
/// </summary>
internal static class EmjStrings
{
    private static readonly string[] Canonical = ["East", "South", "West", "North"];

    // Verified on an English client (self panel read "South"; riichi badge confirmed). Add other
    // languages only from captured panel text, one localized word -> wind index 0..3 (E/S/W/N).
    private static readonly Dictionary<string, int> WindIndex = new(StringComparer.Ordinal)
    {
        ["East"] = 0,
        ["South"] = 1,
        ["West"] = 2,
        ["North"] = 3,
    };

    // Riichi badge substrings, verified on an English client.
    private static readonly string[] RiichiTerms = ["Riichi"];

    /// <summary>The canonical English seat wind for a localized panel word, or null if unrecognized.</summary>
    public static string? CanonicalWind(string? text)
        => text is not null && WindIndex.TryGetValue(text.Trim(), out int i) ? Canonical[i] : null;

    /// <summary>Whether a panel text is a riichi badge in a recognized client language.</summary>
    public static bool IsRiichi(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (string term in RiichiTerms)
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
