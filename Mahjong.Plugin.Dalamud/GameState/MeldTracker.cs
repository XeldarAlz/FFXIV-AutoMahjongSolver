using System.Collections.Generic;
using Mahjong.Engine;

namespace Mahjong.Plugin.Dalamud.GameState;

/// <summary>
/// In-plugin tracker for the player's own open melds within the current round.
/// The Emj addon doesn't surface open-meld records in any memory region we've been
/// able to decode; instead we record melds at the moment the plugin (or the user,
/// via hooked FireCallback) accepts a call prompt. Cleared at round start —
/// detected via a sharp upward jump in the wall remaining count.
/// </summary>
public sealed class MeldTracker
{
    private readonly List<Meld> melds = new();
    private int lastObservedWall = -1;

    public IReadOnlyList<Meld> Melds => melds;

    /// <summary>Record a meld formed by accepting a call prompt.</summary>
    public void Record(Meld meld) => melds.Add(meld);

    /// <summary>
    /// Track wall remaining tick by tick. A sharp upward jump (<paramref name="wallRemaining"/>
    /// exceeding the previous reading by more than 5) means a fresh hand has been
    /// dealt and any previously-tracked melds are stale.
    ///
    /// <para>Replaces the earlier closed-hand-count heuristic, which cleared the
    /// tracker any tick where closed-hand was ≥ 13. That was correct for genuine
    /// round transitions but also fired during the ~5 ms window after a chi/pon
    /// accept, where the FireCallback hook had already recorded the meld but the
    /// addon's hand-array hadn't yet reduced from 13 to 11. The tracker
    /// self-erased the meld it had just received, leaving the discard scorer
    /// staring at an 11-tile hand with melds=0 and panicking with "DiscardScorer
    /// requires a 14-tile hand" — observed 2026-05-09 10:06.</para>
    /// </summary>
    public void ObserveWall(int wallRemaining)
    {
        if (lastObservedWall >= 0 && wallRemaining > lastObservedWall + 5)
            melds.Clear();
        lastObservedWall = wallRemaining;
    }

    /// <summary>Manual reset for commands / tests.</summary>
    public void Clear()
    {
        melds.Clear();
        lastObservedWall = -1;
    }
}
