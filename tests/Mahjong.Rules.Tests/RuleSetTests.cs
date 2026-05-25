namespace Mahjong.Rules.Tests;

public class RuleSetTests
{
    [Fact]
    public void Riichi_registers_thirty_eight_yaku_rules()
    {
        var rules = new RiichiRuleSet();
        Assert.Equal(38, rules.YakuRules.Count);
    }

    [Fact]
    public void Riichi_yaku_ids_are_unique_per_rule_definition()
    {
        var rules = new RiichiRuleSet();
        var ids = rules.YakuRules.Select(r => r.Definition.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Riichi_min_han_is_one()
    {
        Assert.Equal(1, new RiichiRuleSet().MinHan);
    }

    [Fact]
    public void Doman_min_han_is_two()
    {
        Assert.Equal(2, new DomanRuleSet().MinHan);
    }

    [Fact]
    public void Doman_inherits_riichi_yaku_list()
    {
        var riichi = new RiichiRuleSet();
        var doman = new DomanRuleSet();
        Assert.Equal(riichi.YakuRules.Count, doman.YakuRules.Count);
    }

    [Fact]
    public void Both_rulesets_expose_non_null_scoring_dora_and_fu_rules()
    {
        foreach (IRuleSet rules in new IRuleSet[] { new RiichiRuleSet(), new DomanRuleSet() })
        {
            Assert.NotNull(rules.ScoringRule);
            Assert.NotNull(rules.DoraRule);
            Assert.NotNull(rules.FuRule);
        }
    }

    [Fact]
    public void Both_rulesets_cap_yakuman_multiplier_at_two()
    {
        Assert.Equal(2, new RiichiRuleSet().MaxYakuman);
        Assert.Equal(2, new DomanRuleSet().MaxYakuman);
    }
}
