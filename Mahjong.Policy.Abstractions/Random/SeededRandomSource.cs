namespace Mahjong.Policy.Abstractions.Random;

/// <summary>
/// Default <see cref="IRandomSource"/> implementation. Wraps
/// <see cref="System.Random"/>; same statistical properties, plus pinning a
/// seed gives reproducible runs for tuning and tests.
/// </summary>
public sealed class SeededRandomSource : IRandomSource
{
    private readonly System.Random rng;

    /// <summary>Construct with the given seed.</summary>
    public SeededRandomSource(int seed) => rng = new System.Random(seed);

    /// <summary>Construct with a time-based seed (only suitable for live play, never tests).</summary>
    public SeededRandomSource() => rng = new System.Random();

    public int Next() => rng.Next();
    public int Next(int exclusiveUpperBound) => rng.Next(exclusiveUpperBound);
    public int Next(int inclusiveLowerBound, int exclusiveUpperBound)
        => rng.Next(inclusiveLowerBound, exclusiveUpperBound);
    public double NextDouble() => rng.NextDouble();
}
