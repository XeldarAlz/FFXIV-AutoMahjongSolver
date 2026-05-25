using Mahjong.Rules.Scoring;

namespace Mahjong.Rules.Rulesets;

/// <summary>
/// FFXIV Doman: identical to <see cref="RiichiRuleSet"/> except <see cref="MinHan"/>=2.
/// </summary>
public sealed class DomanRuleSet : IRuleSet
{
    private readonly RiichiRuleSet riichi = new();

    public string Name => "Doman";

    public IReadOnlyList<IYakuRule> YakuRules => riichi.YakuRules;
    public IScoringRule ScoringRule => riichi.ScoringRule;
    public IDoraRule DoraRule => riichi.DoraRule;
    public IFuRule FuRule => riichi.FuRule;

    public bool AllowsRedDora => false;
    public bool AllowsKuitan => true;
    public int MinHan => 2;
    public int KazoeThreshold => ScoringConstants.KazoeYakumanHan;
    public int MaxYakuman => 2;
}
