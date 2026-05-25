namespace Mahjong.Replay;

/// <summary>Tile fields are short names ("5m", "1z") — stable across schema changes.</summary>
public sealed record ReplayDecisionEntry(
    int Turn,
    string Actual,
    string Policy,
    bool Matched);

/// <summary>
/// <see cref="Source"/> is the filename only; <see cref="Equal"/> hand-compares because
/// record equality on array members is reference-equal.
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
