using Mahjong.Rules.Scoring;

namespace Mahjong.Rules.Rulesets;

/// <summary>
/// FFXIV Doman Mahjong rule set. Drives the live plugin.
///
/// As of 2026-05-07 this is functionally identical to <see cref="RiichiRuleSet"/>
/// except for the minimum-han threshold — FFXIV's Doman tooltips reference a
/// 2-han minimum (see docs/ruleset.md Q1).
///
/// As more Doman/Riichi deltas are confirmed (see Q2-Q13 in the spec), they are
/// applied here either by:
///   * Removing rules from <see cref="YakuRules"/> (for yaku Doman doesn't recognize),
///   * Replacing rules with Doman-specific implementations (for yaku Doman scores
///     differently), or
///   * Adjusting the structural toggles (<see cref="AllowsKuitan"/>, etc.).
///
/// The composition pattern means each delta is one targeted change, never a
/// fork of the whole detection logic.
/// </summary>
public sealed class DomanRuleSet : IRuleSet
{
    private readonly RiichiRuleSet riichi = new();

    public string Name => "Doman";

    public IReadOnlyList<IYakuRule> YakuRules => riichi.YakuRules;
    public IScoringRule ScoringRule => riichi.ScoringRule;
    public IDoraRule DoraRule => riichi.DoraRule;
    public IFuRule FuRule => riichi.FuRule;

    public bool AllowsRedDora => false;        // unconfirmed (docs/ruleset.md Q5)
    public bool AllowsKuitan => true;           // unconfirmed (docs/ruleset.md Q2) — riichi default
    public int MinHan => 2;                     // Doman delta from riichi (Q1)
    public int KazoeThreshold => ScoringConstants.KazoeYakumanHan;
    public int MaxYakuman => 2;
}
