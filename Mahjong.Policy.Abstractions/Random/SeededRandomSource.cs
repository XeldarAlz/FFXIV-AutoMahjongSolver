namespace Mahjong.Policy.Abstractions.Random;

public sealed class SeededRandomSource : IRandomSource
{
    private readonly System.Random rng;

    public SeededRandomSource(int seed) => rng = new System.Random(seed);

    public SeededRandomSource() => rng = new System.Random();

    public int Next() => rng.Next();
    public int Next(int exclusiveUpperBound) => rng.Next(exclusiveUpperBound);
    public int Next(int inclusiveLowerBound, int exclusiveUpperBound)
        => rng.Next(inclusiveLowerBound, exclusiveUpperBound);
    public double NextDouble() => rng.NextDouble();
}
