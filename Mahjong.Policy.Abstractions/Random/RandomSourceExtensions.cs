namespace Mahjong.Policy.Abstractions.Random;

public static class RandomSourceExtensions
{
    public static void Shuffle<T>(this IRandomSource rng, IList<T> list)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(list);

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Draw uniformly from a live-tile pool; null if empty.</summary>
    public static Tile? DrawFromLive(this IRandomSource rng, ReadOnlySpan<int> live)
    {
        ArgumentNullException.ThrowIfNull(rng);

        int total = 0;
        for (int k = 0; k < live.Length; k++)
            total += live[k] > 0 ? live[k] : 0;
        if (total == 0)
            return null;

        int pick = rng.Next(total);
        for (int k = 0; k < live.Length; k++)
        {
            int count = live[k] > 0 ? live[k] : 0;
            if (pick < count)
                return Tile.FromId(k);
            pick -= count;
        }
        return null;
    }
}
