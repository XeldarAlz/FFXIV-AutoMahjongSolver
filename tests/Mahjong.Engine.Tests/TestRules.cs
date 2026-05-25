namespace Mahjong.Engine.Tests;

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
