using Mahjong.Policy.Mcts;

namespace Mahjong.Policy.Tests;

public class MctsNodePoolTests
{
    [Fact]
    public void Rent_from_empty_pool_creates_a_fresh_node()
    {
        var pool = new MctsNodePool();
        Assert.Equal(0, pool.Count);
        var node = pool.Rent();
        Assert.NotNull(node);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void Return_pushes_a_reset_node_back()
    {
        var pool = new MctsNodePool();
        var node = pool.Rent();
        node.Init(StateSnapshot.Empty, parent: null, action: Tile.FromId(5));
        node.Visits = 7;
        node.TotalValue = 42.0;

        pool.Return(node);

        Assert.Equal(1, pool.Count);
        Assert.Equal(0, node.Visits);
        Assert.Equal(0, node.TotalValue);
        Assert.Null(node.Action);
    }

    [Fact]
    public void Rent_after_Return_recycles_the_same_instance()
    {
        var pool = new MctsNodePool();
        var first = pool.Rent();
        pool.Return(first);

        var second = pool.Rent();
        Assert.Same(first, second);
    }

    [Fact]
    public void Pool_supports_repeated_rent_and_return_cycles()
    {
        var pool = new MctsNodePool();

        for (int cycle = 0; cycle < 5; cycle++)
        {
            var rented = new List<MctsNode>();
            for (int i = 0; i < 10; i++)
                rented.Add(pool.Rent());
            foreach (var n in rented)
                pool.Return(n);
        }

        // Across all cycles we should have created exactly 10 distinct nodes.
        // (After cycle 1 every subsequent cycle hits the pool.)
        Assert.Equal(10, pool.Count);
    }
}
