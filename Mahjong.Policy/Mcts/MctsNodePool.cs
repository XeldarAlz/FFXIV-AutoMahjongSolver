namespace Mahjong.Policy.Mcts;

/// <summary>
/// Stack-backed object pool for <see cref="MctsNode"/>. Single-threaded —
/// MCTS search is per-search, not per-thread, so contention isn't a concern.
///
/// <see cref="Return"/> resets the node before pushing it back, so callers
/// don't need to remember to reset.
/// </summary>
public sealed class MctsNodePool : INodePool<MctsNode>
{
    private readonly Stack<MctsNode> pool = new();

    public MctsNode Rent()
    {
        if (pool.TryPop(out var node))
            return node;
        return new MctsNode();
    }

    public void Return(MctsNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Reset();
        pool.Push(item);
    }

    /// <summary>Diagnostic — current pool size, mostly for tests.</summary>
    public int Count => pool.Count;
}
