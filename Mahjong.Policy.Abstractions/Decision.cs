namespace Mahjong.Policy.Abstractions;

public sealed record Decision<T>(bool Accept, T Value, Reason Reason);

/// <summary><see cref="Code"/> is stable for machines; <see cref="Display"/> is UI text.</summary>
public sealed record Reason(
    string Code,
    string Display,
    IReadOnlyDictionary<string, object>? Data = null)
{
    public static Reason Empty { get; } = new("none", string.Empty);
}
