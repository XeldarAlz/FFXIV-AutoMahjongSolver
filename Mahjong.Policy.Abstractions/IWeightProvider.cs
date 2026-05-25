using Mahjong.Policy.Abstractions.Weights;

namespace Mahjong.Policy.Abstractions;

public interface IWeightProvider
{
    WeightBundle Current { get; }

    event Action<WeightBundle>? Changed;
}

public sealed class DefaultWeightProvider : IWeightProvider
{
    public WeightBundle Current => WeightBundle.Default;
    public event Action<WeightBundle>? Changed { add { } remove { } }
}
