using AutoMahjongSolver.Core;
using AutoMahjongSolver.Core.Game;
using AutoMahjongSolver.Engine;

namespace AutoMahjongSolver.Windows.Sections;

internal enum BoardPhase
{
    NoMatch,
    Watching,
    OurTurn,
    CallOffered,
    BetweenHands,
}

/// <summary>
/// Throttled read of the live Doman Mahjong board, shared by the idle and running panels. Derives the
/// same decision the <see cref="AutoPlayController"/> would make so the cockpit shows what the bot is about to do.
/// </summary>
internal sealed class LiveBoard
{
    private const int CallLabelIndex = 7; // AtkValues slot holding the offered-action label in state 15
    private const int UnreadableGraceMs = 1500; // ignore transient decode gaps (deals, animations)
    private const int ReadIntervalMs = 300;
    private readonly HandAnalyzer analyzer = new();
    private long lastTick;
    private long unreadableSince;

    public bool Present { get; private set; }
    public int State { get; private set; } = -1;
    public BoardPhase Phase { get; private set; } = BoardPhase.NoMatch;

    public bool HandRead { get; private set; }

    /// <summary>Hand tiles are on screen but none decode to a known texture (persisted) — an incompatible tile design or a patch.</summary>
    public bool TilesUnreadable { get; private set; }
    public HandSnapshot Snapshot { get; private set; }
    public HandReport Report { get; private set; } = new();

    public bool HandComplete { get; private set; }
    public int BestDiscard { get; private set; } = -1;
    public bool RonOffered { get; private set; }

    public void Refresh()
    {
        long now = Environment.TickCount64;
        if (now - lastTick < ReadIntervalMs)
            return;
        lastTick = now;

        Present = AddonPresent();
        State = Present ? EmjBoardReader.ReadStateCode() : -1;
        Phase = MapPhase(Present, State);

        HandComplete = false;
        BestDiscard = -1;
        RonOffered = false;

        HandSnapshot snap = default;
        bool read = Present && EmjBoardReader.TryReadHand(out snap);
        HandRead = read;
        Snapshot = read ? snap : default;

        bool unreadableNow = Present && !read && snap.VisibleNodes > 0;
        if (unreadableNow)
        {
            if (unreadableSince == 0)
                unreadableSince = now;
            TilesUnreadable = now - unreadableSince >= UnreadableGraceMs;
        }
        else
        {
            unreadableSince = 0;
            TilesUnreadable = false;
        }

        if (HandRead)
        {
            Report = analyzer.Analyze(Snapshot.Counts, Snapshot.MeldCount);
            if (Phase == BoardPhase.OurTurn)
            {
                HandComplete = Report.Shanten == ShantenCalculator.Complete;
                if (!HandComplete && Report.Shape == HandShape.Discard && Report.Discards.Count > 0)
                    BestDiscard = Report.Discards[0].Tile;
            }
        }
        else
        {
            Report = new HandReport();
        }

        if (Phase == BoardPhase.CallOffered)
        {
            string? label = EmjBoardReader.ReadActionLabel(CallLabelIndex);
            RonOffered = label is not null && label.Contains("Ron", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static BoardPhase MapPhase(bool present, int state)
    {
        if (!present)
            return BoardPhase.NoMatch;

        // 6 and 30 are the same discard turn; 15 is a call decision; 2/29/32 are result / new-hand screens.
        return state switch
        {
            6 or 30 => BoardPhase.OurTurn,
            15 => BoardPhase.CallOffered,
            2 or 29 or 32 => BoardPhase.BetweenHands,
            _ => BoardPhase.Watching,
        };
    }

    private static bool AddonPresent()
    {
        unsafe
        {
            return EmjBoardReader.TryGetAddon(out _);
        }
    }
}
