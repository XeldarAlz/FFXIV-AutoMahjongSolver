namespace Mahjong.Policy.Abstractions;

public enum ActionKind : byte
{
    Pass,
    Discard,
    Riichi,
    Tsumo,
    Ron,
    Pon,
    Chi,
    AnKan,
    MinKan,
    ShouMinKan,
}

/// <summary>
/// A policy's final decision on the current turn. Immutable.
///
/// <see cref="Reasoning"/> is the human-readable summary for the debug overlay
/// and chat output. <see cref="Steps"/> carries the structured per-evaluator
/// reasons (call/riichi/push-fold/discard) so the UI can render the rationale
/// chain without parsing strings.
/// </summary>
public sealed record ActionChoice(
    ActionKind Kind,
    Tile? DiscardTile = null,
    MeldCandidate? Call = null,
    string Reasoning = "",
    IReadOnlyList<Reason>? Steps = null)
{
    public IReadOnlyList<Reason> ReasonSteps => Steps ?? [];

    public static ActionChoice Pass(string why = "", IReadOnlyList<Reason>? steps = null) =>
        new(ActionKind.Pass, Reasoning: why, Steps: steps);

    public static ActionChoice Discard(Tile t, string why = "", IReadOnlyList<Reason>? steps = null) =>
        new(ActionKind.Discard, DiscardTile: t, Reasoning: why, Steps: steps);

    public static ActionChoice DeclareRiichi(Tile discard, string why = "", IReadOnlyList<Reason>? steps = null) =>
        new(ActionKind.Riichi, DiscardTile: discard, Reasoning: why, Steps: steps);

    public static ActionChoice DeclareTsumo(string why = "") =>
        new(ActionKind.Tsumo, Reasoning: why);

    public static ActionChoice DeclareRon(string why = "") =>
        new(ActionKind.Ron, Reasoning: why);
}
