namespace Mahjong.Engine.Tests;

/// <summary>
/// Shared rule-set fixture for Engine tests. Tests run against the standard
/// <see cref="RiichiRuleSet"/> — Doman-specific behavior is tested separately
/// in Mahjong.Rules.Tests.
/// </summary>
internal static class TestRules
{
    public static readonly RiichiRuleSet RuleSet = new();
    public static readonly Scorer Scorer = new(RuleSet);

    public static int Fu(Decomposition d, WinContext ctx, IReadOnlyList<YakuHit>? yaku = null)
        => RuleSet.FuRule.Compute(d, ctx, yaku ?? []);

    public static ScoringTier Tier(int han, int fu, bool isYakuman = false)
        => RuleSet.ScoringRule.ResolveTier(han, fu, isYakuman);

    public static Payments Pay(ScoringTier tier, bool isDealer, WinKind kind)
        => RuleSet.ScoringRule.Pay(tier, isDealer, kind);

    public static IReadOnlyList<YakuHit> WithPinfu()
        => [new YakuHit(Yaku.Pinfu, 1)];
}
