namespace Mahjong.Replay;

/// <summary>
/// One discard decision in a recorded kyoku, captured for golden-file storage.
/// Tile fields are short names (e.g. "5m", "2p", "1z") — stable across schema
/// changes and trivially diff-readable in the JSON file.
/// </summary>
public sealed record ReplayDecisionEntry(
    int Turn,
    string Actual,
    string Policy,
    bool Matched);

/// <summary>
/// JSON-serializable trace of a policy's decisions over one replay run. The
/// golden file format the regression suite reads and writes.
///
/// <see cref="Source"/> is the Tenhou log filename (not the full path) — keeps
/// the snapshot portable across checkout locations.
///
/// <see cref="Equal"/> compares two snapshots field-by-field on every entry,
/// because record equality on collection-typed members is reference-equal.
/// </summary>
public sealed record ReplaySnapshot(
    string Source,
    int Seat,
    int TotalDecisions,
    int Matches,
    double Accuracy,
    ReplayDecisionEntry[] Decisions)
{
    public static bool Equal(ReplaySnapshot a, ReplaySnapshot b)
    {
        if (a.Source != b.Source ||
            a.Seat != b.Seat ||
            a.TotalDecisions != b.TotalDecisions ||
            a.Matches != b.Matches ||
            Math.Abs(a.Accuracy - b.Accuracy) > 1e-4 ||
            a.Decisions.Length != b.Decisions.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Decisions.Length; i++)
        {
            if (a.Decisions[i] != b.Decisions[i])
                return false;
        }
        return true;
    }
}
