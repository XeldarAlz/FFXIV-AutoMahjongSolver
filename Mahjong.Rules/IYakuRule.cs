namespace Mahjong.Rules;

/// <summary>
/// One yaku detection rule. Each rule owns the logic for a single yaku
/// (e.g. Pinfu, Tanyao, Suuankou) and is registered with an <see cref="IRuleSet"/>.
///
/// <see cref="Detect"/> returns zero or more hits — most rules return at most one,
/// but some (Yakuhai) emit a hit per qualifying group.
/// </summary>
public interface IYakuRule
{
    /// <summary>Static metadata: the yaku id, display name, han values.</summary>
    YakuDefinition Definition { get; }

    /// <summary>Does this rule fire for the given decomposition + context?</summary>
    IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx);

    /// <summary>
    /// Yaku that should be removed when this rule fires. The orchestrator applies
    /// these post-detection. Examples: Ryanpeikou supersedes Iipeiko; Junchan
    /// supersedes Chanta; Chinitsu supersedes Honitsu.
    /// </summary>
    IReadOnlyList<Yaku> Conflicts => [];
}

/// <summary>
/// Static metadata about a yaku — the parts that don't depend on the hand.
///
/// <see cref="ClosedHan"/> is the han value when the hand is concealed.
/// <see cref="OpenHan"/> is the han value when the hand has any open meld.
/// A value of 0 in <see cref="OpenHan"/> means the yaku is closed-only.
/// </summary>
public sealed record YakuDefinition(
    Yaku Id,
    string Name,
    int ClosedHan,
    int OpenHan,
    bool IsYakuman = false,
    bool RequiresMenzen = false)
{
    /// <summary>Resolve han for this yaku given whether the hand is closed.</summary>
    public int Han(bool isMenzen) => isMenzen ? ClosedHan : OpenHan;
}
