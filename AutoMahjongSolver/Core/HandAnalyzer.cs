using AutoMahjongSolver.Engine;

namespace AutoMahjongSolver.Core;

internal enum HandShape
{
    Discard, // 14-tile (post-draw): rank discards
    Wait,    // 13-tile: show acceptance
    Other,   // incomplete count
}

internal sealed class HandReport
{
    public int Total { get; init; }
    public int Shanten { get; init; }
    public HandShape Shape { get; init; }
    public IReadOnlyList<DiscardOption> Discards { get; init; } = Array.Empty<DiscardOption>();
    public Acceptance Wait { get; init; }
}

/// <summary>
/// Caches the solver result for a hand and only recomputes when the tiles change. The shanten/ukeire
/// search is expensive, so it must never run unconditionally inside an ImGui Draw loop.
/// </summary>
internal sealed class HandAnalyzer
{
    private readonly int[] lastCounts = new int[Tiles.TypeCount];
    private int lastMelds = -1;
    private bool primed;
    private HandReport cached = new();

    public HandReport Analyze(int[] counts, int meldCount = 0)
    {
        if (primed && meldCount == lastMelds && SameAsLast(counts))
            return cached;

        int total = counts.Sum();
        int shanten = ShantenCalculator.Calculate(counts, meldCount);

        HandShape shape;
        IReadOnlyList<DiscardOption> discards = Array.Empty<DiscardOption>();
        Acceptance wait = default;

        if (total % 3 == 2)
        {
            shape = HandShape.Discard;
            discards = UkeireCalculator.BestDiscards(counts, meldCount);
        }
        else if (total % 3 == 1)
        {
            shape = HandShape.Wait;
            wait = UkeireCalculator.Accept(counts, meldCount);
        }
        else
        {
            shape = HandShape.Other;
        }

        cached = new HandReport
        {
            Total = total,
            Shanten = shanten,
            Shape = shape,
            Discards = discards,
            Wait = wait,
        };

        Array.Copy(counts, lastCounts, Tiles.TypeCount);
        lastMelds = meldCount;
        primed = true;
        return cached;
    }

    private bool SameAsLast(int[] counts)
    {
        for (int i = 0; i < Tiles.TypeCount; i++)
            if (counts[i] != lastCounts[i])
                return false;
        return true;
    }
}
