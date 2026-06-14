using AutoMahjongSolver.Engine;
using Xunit;

namespace AutoMahjongSolver.Engine.Tests;

public class DangerTests
{
    private static IReadOnlyList<IReadOnlyList<int>> Rivers(params string[] notations)
        => notations.Select(IReadOnlyList<int> (n) => CountsToList(Tiles.ParseCounts(n))).ToList();

    private static List<int> CountsToList(int[] counts)
    {
        var list = new List<int>();
        for (int t = 0; t < Tiles.TypeCount; t++)
            for (int n = 0; n < counts[t]; n++)
                list.Add(t);
        return list;
    }

    private static int Danger(string tile, IReadOnlyList<IReadOnlyList<int>> threats, int[]? seen = null)
        => DangerCalculator.Danger(Tiles.Index(tile), threats, seen ?? new int[Tiles.TypeCount]);

    [Fact]
    public void Genbutsu_IsZero()
    {
        var threats = Rivers("159m12z");
        Assert.Equal(0, Danger("5m", threats));
        Assert.Equal(0, Danger("1z", threats));
    }

    [Fact]
    public void Genbutsu_MustHoldAgainstEveryThreat()
    {
        var threats = Rivers("5m", "1z");
        Assert.NotEqual(0, Danger("5m", threats)); // safe vs first river only
    }

    [Fact]
    public void Honors_GetSaferAsCopiesAreSeen()
    {
        var threats = Rivers("123m");
        var seen = new int[Tiles.TypeCount];

        int live = Danger("7z", threats, seen);
        seen[Tiles.Index("7z")] = 2;
        int twoSeen = Danger("7z", threats, seen);
        seen[Tiles.Index("7z")] = 3;
        int dead = Danger("7z", threats, seen);

        Assert.True(dead < twoSeen);
        Assert.True(twoSeen < live);
    }

    [Fact]
    public void SujiTerminal_SaferThanLiveTerminal_AndMiddleIsWorst()
    {
        var sujiThreat = Rivers("4m"); // 4m discarded -> 1m is suji
        var bare = Rivers("9s");

        int suji1m = Danger("1m", sujiThreat);
        int live1m = Danger("1m", bare);
        int live5m = Danger("5m", bare);

        Assert.True(suji1m < live1m);
        Assert.True(live1m < live5m);
    }

    [Fact]
    public void MiddleTile_NeedsBothSujiSides()
    {
        Assert.Equal(85, Danger("5m", Rivers("2m")));   // only one side
        Assert.Equal(45, Danger("5m", Rivers("28m")));  // both 2m and 8m discarded
    }

    [Fact]
    public void NearlyDeadNumberTile_GetsDiscount()
    {
        var threats = Rivers("9s");
        var seen = new int[Tiles.TypeCount];
        seen[Tiles.Index("5m")] = 3;
        Assert.Equal(70, Danger("5m", threats, seen)); // 85 - 15
    }
}
