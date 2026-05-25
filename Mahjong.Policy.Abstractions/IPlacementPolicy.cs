namespace Mahjong.Policy.Abstractions;

/// <summary>Discard-scorer multipliers biasing play by rank context (rank 1 defends, rank 4 chases).</summary>
public readonly record struct PlacementMultipliers(
    double Danger,
    double Ukeire,
    double HandValue)
{
    public static PlacementMultipliers Neutral => new(1.0, 1.0, 1.0);
}

public interface IPlacementPolicy
{
    PlacementMultipliers ComputeFor(StateSnapshot state);
}
