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
/// <see cref="Reasoning"/> is a human summary; <see cref="Steps"/> is the structured
/// per-evaluator rationale chain.
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
