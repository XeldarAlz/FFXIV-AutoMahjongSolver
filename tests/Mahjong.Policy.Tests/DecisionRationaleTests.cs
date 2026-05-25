using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tests;

public class DecisionRationaleTests
{
    private static readonly EfficiencyPolicy Policy = new();

    [Fact]
    public void Discard_choice_has_at_least_a_pushfold_and_discard_step()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var choice = Policy.Choose(s);

        Assert.Equal(ActionKind.Discard, choice.Kind);
        Assert.NotEmpty(choice.ReasonSteps);

        Assert.Contains(choice.ReasonSteps, r => r.Code.Contains("push") || r.Code.Contains("fold") || r.Code.Contains("tenpai-push"));
        Assert.Contains(choice.ReasonSteps, r => r.Code == "discard");
    }

    [Fact]
    public void Reason_data_dictionary_accessible_by_code()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var choice = Policy.Choose(s);

        var discardStep = choice.ReasonSteps.First(r => r.Code == "discard");
        Assert.False(string.IsNullOrEmpty(discardStep.Display));
    }

    [Fact]
    public void Tsumo_shortcircuit_skips_substep_chain()
    {
        var s = Snapshots.Closed14("123m456p789s11123p", ActionFlags.Tsumo);
        var choice = Policy.Choose(s);

        Assert.Equal(ActionKind.Tsumo, choice.Kind);
        Assert.Empty(choice.ReasonSteps);
    }
}
