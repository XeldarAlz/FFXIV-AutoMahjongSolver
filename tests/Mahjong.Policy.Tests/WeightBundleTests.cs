namespace Mahjong.Policy.Tests;

public class WeightBundleTests
{
    [Fact]
    public void Default_bundle_carries_each_subweight_default()
    {
        var b = WeightBundle.Default;
        Assert.Same(DiscardWeights.Default, b.Discard);
        Assert.Same(OpponentWeights.Default, b.Opponent);
        Assert.Same(PlacementWeights.Default, b.Placement);
        Assert.Same(RolloutWeights.Default, b.Rollout);
    }

    [Fact]
    public void Default_schema_version_matches_current()
    {
        Assert.Equal(WeightBundle.CurrentSchemaVersion, WeightBundle.Default.SchemaVersion);
    }

    [Fact]
    public void Discard_default_preserves_tuned_values()
    {
        // Pin the post-tuner-pass values so a careless edit to the defaults
        // surfaces immediately rather than silently drifting policy behavior.
        var d = DiscardWeights.Default;
        Assert.Equal(100.0, d.Shanten);
        Assert.InRange(d.UkeireKinds, 0.19, 0.20);
        Assert.InRange(d.UkeireWeighted, 0.50, 0.51);
        Assert.InRange(d.Dora, 36.0, 38.0);
        Assert.InRange(d.Yakuhai, 18.0, 20.0);
        Assert.InRange(d.IsolatedTerminal, 53.0, 56.0);
        Assert.InRange(d.DealInCost, 0.018, 0.021);
    }

    [Fact]
    public void Opponent_defaults_match_pre_phase3_hand_tuned_values()
    {
        var o = OpponentWeights.Default;
        Assert.Equal(-2.0, o.TenpaiIntercept);
        Assert.Equal(0.08, o.TenpaiDiscardCount);
        Assert.Equal(0.35, o.TenpaiMeldCount);
        Assert.Equal(0.02, o.TenpaiTurnsElapsed);
        Assert.Equal(4000.0, o.ExpectedHandValue);
    }

    [Fact]
    public void Placement_defaults_have_neutral_for_rank_2_or_3_mid_hanchan()
    {
        var p = PlacementWeights.Default;
        Assert.Equal(PlacementMultipliers.Neutral, p.Rank2Or3);
        Assert.Equal(8000, p.Rank1HugeLeadGap);
    }

    [Fact]
    public void Rollout_defaults_carry_pre_phase3_baked_values()
    {
        var r = RolloutWeights.Default;
        Assert.Equal(-100.0, r.ShantenPenalty);
        Assert.Equal(1.0, r.UkeireBonus);
    }

    [Fact]
    public void Bundle_with_record_substitution_replaces_only_targeted_subweights()
    {
        var custom = new DiscardWeights(
            Shanten: 50.0, UkeireKinds: 1.0, UkeireWeighted: 1.0,
            Dora: 1.0, Yakuhai: 1.0, IsolatedTerminal: 1.0, DealInCost: 1.0);
        var b = WeightBundle.Default with { Discard = custom };

        Assert.Same(custom, b.Discard);
        Assert.Same(WeightBundle.Default.Opponent, b.Opponent);
    }
}
