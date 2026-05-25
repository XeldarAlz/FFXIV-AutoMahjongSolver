namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>Pure variant (double yakuman): winning tile is the extra, leaving a 9-sided wait.</summary>
public sealed class ChuurenPoutouRule : IYakuRule
{
    private static readonly int[] Baseline = [3, 1, 1, 1, 1, 1, 1, 1, 3];

    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.ChuurenPoutou,
        Name: "Chuuren Poutou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (!d.IsMenzen || d.Form != DecompositionForm.Standard)
            return [];

        var counts = ReconstructCounts(d);
        if (!TryFindSingleSuitedSuit(counts, out int suit))
            return [];

        int suitBase = suit * TileIds.SuitSize;
        if (!MatchesBaselinePlusOneExtra(counts, suitBase))
            return [];

        bool isPure = WinningTileIsTheExtra(counts, ctx.WinningTile, suitBase);

        int han = isPure ? HanValues.DoubleYakuman : HanValues.Yakuman;
        return [new YakuHit(Definition.Id, han, IsYakuman: true)];
    }

    private static int[] ReconstructCounts(Decomposition d)
    {
        var counts = new int[Tile.Count34];
        foreach (var g in d.Groups)
            foreach (var t in g.Tiles)
                counts[t.Id]++;
        return counts;
    }

    private static bool TryFindSingleSuitedSuit(int[] counts, out int suit)
    {
        int? found = null;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;
            if (i >= TileIds.HonorStart)
            {
                suit = -1;
                return false;
            }

            int s = i / TileIds.SuitSize;
            if (found is null)
                found = s;
            else if (found != s)
            {
                suit = -1;
                return false;
            }
        }

        if (found is null)
        {
            suit = -1;
            return false;
        }
        suit = found.Value;
        return true;
    }

    private static bool MatchesBaselinePlusOneExtra(int[] counts, int suitBase)
    {
        int totalExtra = 0;
        for (int i = 0; i < TileIds.SuitSize; i++)
        {
            int diff = counts[suitBase + i] - Baseline[i];
            if (diff < 0)
                return false;
            totalExtra += diff;
        }
        return totalExtra == 1;
    }

    private static bool WinningTileIsTheExtra(int[] counts, Tile winning, int suitBase)
    {
        int winId = winning.Id;
        if (winId < suitBase || winId >= suitBase + TileIds.SuitSize)
            return false;

        counts[winId]--;
        for (int i = 0; i < TileIds.SuitSize; i++)
        {
            if (counts[suitBase + i] != Baseline[i])
                return false;
        }
        return true;
    }
}
