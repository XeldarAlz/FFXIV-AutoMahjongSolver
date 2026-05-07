namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Object pool for MCTS-style search nodes (or any reset-friendly type). The
/// search algorithm rents at the start of each iteration and returns at the
/// end, avoiding GC pressure from per-determinization tree allocations.
///
/// Implementations are expected to call the rented item's <c>Reset()</c>
/// before handing it back from <see cref="Rent"/>; callers shouldn't need to
/// reset themselves.
/// </summary>
public interface INodePool<T> where T : class
{
    /// <summary>Rent a fresh-or-reset instance.</summary>
    T Rent();

    /// <summary>Return an instance to the pool. Implementations reset state on the way out or in.</summary>
    void Return(T item);
}
