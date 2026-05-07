namespace Mahjong.Rules;

/// <summary>
/// A complete set of mahjong rules — the yaku list, the scoring table, the
/// dora cycle, the fu table, plus the structural toggles (red dora,
/// open-tanyao, minimum han to declare a win, etc.).
///
/// Implementations: <c>RiichiRuleSet</c> (used by Tenhou replay parsing in
/// Mahjong.Replay), <c>DomanRuleSet</c> (used by the live FFXIV plugin).
///
/// A consumer that scores a hand always passes an explicit IRuleSet — there is
/// no global "current rules" state, because the same process scores Tenhou
/// replays under riichi rules in parallel with live Doman play.
/// </summary>
public interface IRuleSet
{
    /// <summary>Display name, e.g. "Riichi" / "Doman".</summary>
    string Name { get; }

    /// <summary>Every yaku rule recognized by this variant.</summary>
    IReadOnlyList<IYakuRule> YakuRules { get; }

    IScoringRule ScoringRule { get; }
    IDoraRule DoraRule { get; }
    IFuRule FuRule { get; }

    /// <summary>Are red 5m / 5p / 5s tiles in play? (Not yet modeled — placeholder.)</summary>
    bool AllowsRedDora { get; }

    /// <summary>If true, an opened tanyao hand still counts the yaku (riichi default).</summary>
    bool AllowsKuitan { get; }

    /// <summary>
    /// Minimum han a hand must have to be declarable. Riichi: 1. Doman:
    /// reportedly 2 (see docs/ruleset.md Q1). The orchestrator returns null
    /// for hands below this threshold.
    /// </summary>
    int MinHan { get; }

    /// <summary>
    /// Han at or above which a non-yakuman hand becomes a counted yakuman.
    /// Riichi: 13. (Doman delta tracked in docs/ruleset.md Q10.)
    /// </summary>
    int KazoeThreshold { get; }

    /// <summary>
    /// Cap on yakuman multiplier — most rule sets allow up to double-yakuman
    /// (Daisuushii, pure Chuuren, Suuankou-tanki).
    /// </summary>
    int MaxYakuman { get; }
}
