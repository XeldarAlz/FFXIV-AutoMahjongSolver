namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Structured outcome of a sub-policy evaluator. Replaces the free-form
/// "Reasoning" string on <see cref="ActionChoice"/> for the inner pipeline:
/// each evaluator (call, riichi, push-fold, ...) returns one of these and the
/// composing policy threads them together.
///
/// <typeparamref name="T"/> is the evaluator's payload — typically a bool
/// for yes/no decisions or a domain type like <c>MeldCandidate?</c> for calls.
/// </summary>
public sealed record Decision<T>(bool Accept, T Value, Reason Reason);

/// <summary>
/// Why an evaluator made the choice it did. <see cref="Code"/> is the stable
/// machine-readable identifier; <see cref="Display"/> is what the UI shows;
/// <see cref="Data"/> is structured context for downstream rendering or logging.
/// </summary>
public sealed record Reason(
    string Code,
    string Display,
    IReadOnlyDictionary<string, object>? Data = null)
{
    public static Reason Empty { get; } = new("none", string.Empty);
}
