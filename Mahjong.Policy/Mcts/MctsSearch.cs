using Mahjong.Engine;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Mcts;

/// <summary>
/// Information-Set MCTS kernel. For each determinization (sampled hidden info)
/// it rents a tree from the <see cref="INodePool{T}"/>, selects via UCB1 with
/// progressive widening, expands into top-K candidate discards, rolls out via
/// the injected <see cref="IRolloutPolicy"/>, and backpropagates. Repeats for
/// <see cref="simsPerDeterminization"/> iterations, then returns every node to
/// the pool before sampling the next determinization.
///
/// Deviations from a textbook MCTS for the MVP:
/// <list type="bullet">
///   <item>No chance nodes — draws are abstracted into rollout.</item>
///   <item>Opponents don't act during rollout — opponent-response modeling is owed.</item>
///   <item>Progressive widening = simple top-K from heuristic scorer.</item>
/// </list>
/// </summary>
public sealed class MctsSearch
{
    private readonly Determinizer determinizer;
    private readonly IRolloutPolicy rolloutPolicy;
    private readonly INodePool<MctsNode> nodePool;
    private readonly int determinizations;
    private readonly int simsPerDeterminization;
    private readonly int topK;
    private readonly double ucbExplorationConstant;
    private readonly double progressiveWideningC;

    public MctsSearch(
        Determinizer determinizer,
        IRolloutPolicy rolloutPolicy,
        int determinizations = 8,
        int simsPerDeterminization = 50,
        int topK = 4,
        double ucbExplorationConstant = 1.4,
        double progressiveWideningC = 1.0,
        INodePool<MctsNode>? nodePool = null)
    {
        ArgumentNullException.ThrowIfNull(determinizer);
        ArgumentNullException.ThrowIfNull(rolloutPolicy);
        this.determinizer = determinizer;
        this.rolloutPolicy = rolloutPolicy;
        this.nodePool = nodePool ?? new MctsNodePool();
        this.determinizations = determinizations;
        this.simsPerDeterminization = simsPerDeterminization;
        this.topK = topK;
        this.ucbExplorationConstant = ucbExplorationConstant;
        this.progressiveWideningC = progressiveWideningC;
    }

    public readonly record struct ActionResult(Tile Discard, double MeanValue, int Visits);

    /// <summary>Run MCTS and return top candidates by mean value. First entry is the pick.</summary>
    public ActionResult[] Run(StateSnapshot root, IOpponentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Build the candidate pool from the fast scorer. Caller is expected to
        // have already updated the opponent model.
        var scored = DiscardScorer.Score(root, opponentModel: model);
        if (scored.Length == 0)
            return [];

        int maxK = Math.Min(topK, scored.Length);
        var candidates = new Tile[scored.Length];
        for (int i = 0; i < scored.Length; i++)
            candidates[i] = scored[i].Discard;

        var totalValue = new double[scored.Length];
        var totalVisits = new int[scored.Length];

        var rentedNodes = new List<MctsNode>(maxK + 1);
        for (int det = 0; det < determinizations; det++)
        {
            var sample = determinizer.Sample(root, model);
            if (sample is null)
                continue;

            RunOneDeterminization(root, model, candidates, maxK, rentedNodes, totalValue, totalVisits);
            ReturnAllNodes(rentedNodes);
        }

        return BuildResults(candidates, totalValue, totalVisits);
    }

    private void RunOneDeterminization(
        StateSnapshot root,
        IOpponentModel model,
        Tile[] candidates,
        int maxK,
        List<MctsNode> rentedNodes,
        double[] totalValue,
        int[] totalVisits)
    {
        var rootNode = RentNode(rentedNodes, root, parent: null, action: null);

        // Seed with one child; progressive widening adds more as visits accrue.
        AddChild(rootNode, candidates[0], rentedNodes);

        for (int sim = 0; sim < simsPerDeterminization; sim++)
        {
            int desired = Math.Min(maxK, Math.Max(1,
                (int)Math.Ceiling(progressiveWideningC * Math.Sqrt(rootNode.Visits + 1))));
            while (rootNode.Children.Count < desired && rootNode.Children.Count < candidates.Length)
                AddChild(rootNode, candidates[rootNode.Children.Count], rentedNodes);

            DescendAndBackprop(rootNode, model);
        }

        for (int i = 0; i < rootNode.Children.Count; i++)
        {
            totalValue[i] += rootNode.Children[i].TotalValue;
            totalVisits[i] += rootNode.Children[i].Visits;
        }
    }

    private void DescendAndBackprop(MctsNode rootNode, IOpponentModel model)
    {
        var path = new List<MctsNode> { rootNode };
        var current = rootNode;

        while (current.Expanded && current.Children.Count > 0)
        {
            current = SelectUcb1(current);
            path.Add(current);
        }

        double value = rolloutPolicy.Run(current.State, model);

        foreach (var n in path)
        {
            n.Visits++;
            n.TotalValue += value;
        }
    }

    private MctsNode RentNode(List<MctsNode> rented, StateSnapshot state, MctsNode? parent, Tile? action)
    {
        var node = nodePool.Rent();
        node.Init(state, parent, action);
        rented.Add(node);
        return node;
    }

    private void AddChild(MctsNode parent, Tile discard, List<MctsNode> rented)
    {
        var childState = ApplyDiscard(parent.State, discard);
        var child = RentNode(rented, childState, parent, discard);
        parent.Children.Add(child);
        parent.Expanded = true;
    }

    private void ReturnAllNodes(List<MctsNode> rented)
    {
        foreach (var node in rented)
            nodePool.Return(node);
        rented.Clear();
    }

    private MctsNode SelectUcb1(MctsNode parent)
    {
        MctsNode best = parent.Children[0];
        double bestScore = best.Ucb1(parent.Visits + 1, ucbExplorationConstant);
        for (int i = 1; i < parent.Children.Count; i++)
        {
            double score = parent.Children[i].Ucb1(parent.Visits + 1, ucbExplorationConstant);
            if (score > bestScore)
            {
                best = parent.Children[i];
                bestScore = score;
            }
        }
        return best;
    }

    private static StateSnapshot ApplyDiscard(StateSnapshot state, Tile discarded)
    {
        var newHand = new Tile[state.Hand.Count - 1];
        int w = 0;
        bool removed = false;
        foreach (var t in state.Hand)
        {
            if (!removed && t.Id == discarded.Id)
            {
                removed = true;
                continue;
            }
            newHand[w++] = t;
        }
        return state with { Hand = newHand };
    }

    private static ActionResult[] BuildResults(Tile[] candidates, double[] totalValue, int[] totalVisits)
    {
        var results = new ActionResult[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            double mean = totalVisits[i] > 0 ? totalValue[i] / totalVisits[i] : double.NegativeInfinity;
            results[i] = new ActionResult(candidates[i], mean, totalVisits[i]);
        }
        Array.Sort(results, (a, b) => b.MeanValue.CompareTo(a.MeanValue));
        return results;
    }
}
