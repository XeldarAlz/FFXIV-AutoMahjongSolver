using Mahjong.Engine;

namespace Mahjong.Policy.Simulator;

/// <summary>Permanent furiten only. Temporary furiten (declined-ron) never fires in self-play.</summary>
public static class FuritenDetector
{
    public static bool IsFuriten(IReadOnlyList<int> closedCounts, int meldCount, IReadOnlyList<Tile> ownDiscards)
    {
        if (ownDiscards.Count == 0)
            return false;

        var counts = new int[Tile.Count34];
        for (int i = 0; i < Tile.Count34; i++)
            counts[i] = closedCounts[i];

        Span<bool> isWait = stackalloc bool[Tile.Count34];
        bool anyWait = false;
        for (int k = 0; k < Tile.Count34; k++)
        {
            if (counts[k] >= Tile.CopiesPerKind)
                continue;
            counts[k]++;
            int std = ShantenCalculator.Standard(counts, meldCount);
            int ci = meldCount == 0 ? ShantenCalculator.Chiitoitsu(counts) : 8;
            int ko = meldCount == 0 ? ShantenCalculator.Kokushi(counts) : 8;
            int s = Math.Min(std, Math.Min(ci, ko));
            counts[k]--;
            if (s < 0)
            {
                isWait[k] = true;
                anyWait = true;
            }
        }
        if (!anyWait)
            return false;

        foreach (var t in ownDiscards)
            if (isWait[t.Id])
                return true;
        return false;
    }
}
