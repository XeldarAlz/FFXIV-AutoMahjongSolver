using Mahjong.Policy.Abstractions.Weights;

namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Source of the active <see cref="WeightBundle"/>. Implementations:
///   * <c>DefaultWeightProvider</c> — returns hardcoded defaults (Mahjong.Policy.Abstractions).
///   * <c>JsonWeightProvider</c> — reads from a weights.json on disk (Mahjong.Policy).
///   * <c>MutableWeightProvider</c> — used by tuner runs that mutate weights in-place.
///
/// Consumers cache <see cref="Current"/> per decision; subscribe to
/// <see cref="Changed"/> to react to live reloads.
/// </summary>
public interface IWeightProvider
{
    WeightBundle Current { get; }

    /// <summary>Fired when the underlying weights change (e.g. JSON reload, tuner step).</summary>
    event Action<WeightBundle>? Changed;
}

/// <summary>
/// Trivial provider that always returns <see cref="WeightBundle.Default"/>.
/// The "no-config" baseline used by tests and as the production fallback when
/// no weights.json is present.
/// </summary>
public sealed class DefaultWeightProvider : IWeightProvider
{
    public WeightBundle Current => WeightBundle.Default;
    public event Action<WeightBundle>? Changed { add { } remove { } }
}
