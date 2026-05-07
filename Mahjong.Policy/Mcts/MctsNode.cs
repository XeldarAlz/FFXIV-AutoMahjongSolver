using Mahjong.Engine;

namespace Mahjong.Policy.Mcts;

/// <summary>
/// Node in the MCTS tree. Decision-node variant — represents "after the action
/// <see cref="Action"/> was taken from the parent." Root nodes have null Action.
///
/// Designed for pooling: parameterless construction, settable state via
/// <see cref="Init"/>, and <see cref="Reset"/> clears every field so the
/// <see cref="MctsNodePool"/> can hand the same instance back out.
/// </summary>
public sealed class MctsNode
{
    public StateSnapshot State { get; private set; } = StateSnapshot.Empty;
    public MctsNode? Parent { get; private set; }
    public Tile? Action { get; private set; }

    public int Visits { get; set; }
    public double TotalValue { get; set; }
    public bool Expanded { get; set; }

    public List<MctsNode> Children { get; } = [];

    public void Init(StateSnapshot state, MctsNode? parent, Tile? action)
    {
        State = state;
        Parent = parent;
        Action = action;
    }

    public void Reset()
    {
        State = StateSnapshot.Empty;
        Parent = null;
        Action = null;
        Visits = 0;
        TotalValue = 0;
        Expanded = false;
        Children.Clear();
    }

    public double MeanValue => Visits == 0 ? 0 : TotalValue / Visits;

    /// <summary>UCB1 score with parent N used for exploration.</summary>
    public double Ucb1(int parentVisits, double c)
    {
        if (Visits == 0)
            return double.PositiveInfinity;
        double exploit = MeanValue;
        double explore = c * Math.Sqrt(Math.Log(parentVisits) / Visits);
        return exploit + explore;
    }
}
