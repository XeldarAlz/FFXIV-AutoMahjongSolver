namespace AutoMahjongSolver.Engine;

/// <summary>
/// Heuristic deal-in danger of discarding a tile against one or more riichi opponents.
/// Lower is safer; 0 means genbutsu (in every threat's river, cannot deal in). The scale follows
/// standard riichi defense theory: genbutsu &lt; dead honors &lt; suji terminals &lt; ... &lt; live middle tiles.
/// </summary>
public static class DangerCalculator
{
    public static int Danger(int tile, IReadOnlyList<IReadOnlyList<int>> threatRivers, ReadOnlySpan<int> seen)
    {
        if (threatRivers.Count > 0 && GenbutsuAgainstAll(tile, threatRivers))
            return 0;

        if (Tiles.IsHonor(tile))
        {
            return seen[tile] switch
            {
                >= 3 => 5,
                2 => 15,
                _ => 40,
            };
        }

        bool suji = threatRivers.Count > 0 && SujiAgainstAll(tile, threatRivers);
        int danger = Tiles.Rank(tile) switch
        {
            1 or 9 => suji ? 20 : 45,
            2 or 8 => suji ? 30 : 60,
            3 or 7 => suji ? 35 : 70,
            _ => suji ? 45 : 85,
        };

        // All copies accounted for elsewhere: shanpon/tanki on it are impossible.
        if (seen[tile] >= 3)
            danger -= 15;

        return Math.Max(danger, 1);
    }

    private static bool GenbutsuAgainstAll(int tile, IReadOnlyList<IReadOnlyList<int>> threats)
    {
        foreach (IReadOnlyList<int> river in threats)
            if (!river.Contains(tile))
                return false;
        return true;
    }

    /// <summary>
    /// Suji: every two-sided wait that could ron this tile is voided by the threat's own river.
    /// Middle tiles (4-6) need the suji tile on both sides; edge-ish tiles only the live side.
    /// </summary>
    private static bool SujiAgainstAll(int tile, IReadOnlyList<IReadOnlyList<int>> threats)
    {
        int rank = Tiles.Rank(tile);
        foreach (IReadOnlyList<int> river in threats)
        {
            if (rank <= 3 && !river.Contains(tile + 3))
                return false;
            if (rank >= 7 && !river.Contains(tile - 3))
                return false;
            if (rank is >= 4 and <= 6 && (!river.Contains(tile - 3) || !river.Contains(tile + 3)))
                return false;
        }

        return true;
    }
}
