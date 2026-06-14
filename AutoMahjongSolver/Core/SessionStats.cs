namespace AutoMahjongSolver.Core;

internal enum AutoActionKind
{
    Discard,
    Tsumo,
    Ron,
    Pass,
    Advance,
    Riichi,
    Meld,
}

internal readonly record struct AutoAction(long Tick, AutoActionKind Kind, string Label);

/// <summary>
/// Counters and a short action trail for the current auto-play session. Feeds the autopilot
/// cockpit so it can show what the bot has done without re-rendering the board.
/// </summary>
internal sealed class SessionStats
{
    private const int RecentCap = 6;

    private readonly List<AutoAction> recent = [];
    private long startedAt = Environment.TickCount64;

    public int Discards { get; private set; }
    public int Tsumo { get; private set; }
    public int Ron { get; private set; }
    public int Passes { get; private set; }
    public int Advances { get; private set; }
    public int Riichis { get; private set; }
    public int Melds { get; private set; }

    public int Wins => Tsumo + Ron;
    public bool HasActivity => Discards + Tsumo + Ron + Passes + Advances + Riichis + Melds > 0;
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Environment.TickCount64 - startedAt);

    /// <summary>Newest first.</summary>
    public IReadOnlyList<AutoAction> Recent => recent;

    public void Reset()
    {
        startedAt = Environment.TickCount64;
        Discards = Tsumo = Ron = Passes = Advances = Riichis = Melds = 0;
        recent.Clear();
    }

    public void Record(AutoActionKind kind, string label)
    {
        switch (kind)
        {
            case AutoActionKind.Discard: Discards++; break;
            case AutoActionKind.Tsumo: Tsumo++; break;
            case AutoActionKind.Ron: Ron++; break;
            case AutoActionKind.Pass: Passes++; break;
            case AutoActionKind.Advance: Advances++; break;
            case AutoActionKind.Riichi: Riichis++; break;
            case AutoActionKind.Meld: Melds++; break;
        }

        recent.Insert(0, new AutoAction(Environment.TickCount64, kind, label));
        if (recent.Count > RecentCap)
            recent.RemoveAt(recent.Count - 1);
    }
}
