using Mahjong.Rules.YakuRules;

namespace Mahjong.Rules.Tests;

/// <summary>
/// The conflict graph encodes which yaku supersede which. These tests pin
/// the declarations so accidental edits to a rule's <see cref="IYakuRule.Conflicts"/>
/// list — which would silently change scoring outputs — get caught.
/// </summary>
public class ConflictTests
{
    [Fact]
    public void Ryanpeikou_supersedes_Iipeiko()
    {
        var rule = new RyanpeikouRule();
        Assert.Contains(Yaku.Iipeiko, rule.Conflicts);
        Assert.Single(rule.Conflicts);
    }

    [Fact]
    public void Junchan_supersedes_Chanta()
    {
        var rule = new JunchanRule();
        Assert.Contains(Yaku.Chanta, rule.Conflicts);
        Assert.Single(rule.Conflicts);
    }

    [Fact]
    public void Chinitsu_supersedes_Honitsu()
    {
        var rule = new ChinitsuRule();
        Assert.Contains(Yaku.Honitsu, rule.Conflicts);
        Assert.Single(rule.Conflicts);
    }

    [Fact]
    public void Most_rules_have_no_conflicts()
    {
        // Sanity check: only the three superseding rules above declare conflicts.
        // If a fourth shows up, we should know.
        var rules = new RiichiRuleSet().YakuRules;
        var withConflicts = rules.Where(r => r.Conflicts.Count > 0).ToList();
        Assert.Equal(3, withConflicts.Count);
    }
}
