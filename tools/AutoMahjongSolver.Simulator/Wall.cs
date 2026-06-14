namespace AutoMahjongSolver.Simulator;

/// <summary>Physical tile ids 0-135: type = id/4; copy 0 of each five is the red one (FFXIV has one aka per suit).</summary>
internal static class TileId
{
    public static int Type(int id) => id / 4;

    public static bool IsAka(int id) => id % 4 == 0 && (id / 4) is 4 or 13 or 22;
}

/// <summary>
/// A seeded, shuffled wall. The last 14 tiles are the dead wall: its top two are the dora and ura
/// indicators (no kan in the simulated ruleset, so no extra flips are needed). 70 live draws after
/// the four 13-tile deals — matching the real table.
/// </summary>
internal sealed class Wall
{
    private readonly int[] order = new int[136];
    private readonly int liveEnd;
    private int next;

    public Wall(Random rng)
    {
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        liveEnd = order.Length - 14;
        DoraIndicatorType = TileId.Type(order[^1]);
        UraIndicatorType = TileId.Type(order[^2]);
    }

    public int DoraIndicatorType { get; }
    public int UraIndicatorType { get; }

    public bool HasDraws => next < liveEnd;

    public int Draw() => order[next++];
}
