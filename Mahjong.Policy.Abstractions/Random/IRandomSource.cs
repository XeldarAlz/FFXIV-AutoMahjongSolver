namespace Mahjong.Policy.Abstractions.Random;

public interface IRandomSource
{
    int Next();

    /// <summary>[0, exclusiveUpperBound).</summary>
    int Next(int exclusiveUpperBound);

    /// <summary>[inclusiveLowerBound, exclusiveUpperBound).</summary>
    int Next(int inclusiveLowerBound, int exclusiveUpperBound);

    /// <summary>[0.0, 1.0).</summary>
    double NextDouble();
}
