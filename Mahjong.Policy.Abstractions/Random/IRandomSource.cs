namespace Mahjong.Policy.Abstractions.Random;

/// <summary>
/// Pluggable randomness. Replaces every <c>new System.Random(...)</c> call in
/// the codebase, so RNG-driven components are reproducible (tests pin a seed)
/// and swappable (deterministic mock for unit tests, secure RNG for live play).
///
/// The interface mirrors <see cref="System.Random"/>'s minimal surface — Next,
/// Next(max), NextDouble. Higher-level helpers (Shuffle, draw-from-pool) are
/// extension methods on this interface so they only get implemented once.
/// </summary>
public interface IRandomSource
{
    /// <summary>Random non-negative int.</summary>
    int Next();

    /// <summary>Random int in <c>[0, exclusiveUpperBound)</c>.</summary>
    int Next(int exclusiveUpperBound);

    /// <summary>Random int in <c>[inclusiveLowerBound, exclusiveUpperBound)</c>.</summary>
    int Next(int inclusiveLowerBound, int exclusiveUpperBound);

    /// <summary>Random double in <c>[0.0, 1.0)</c>.</summary>
    double NextDouble();
}
