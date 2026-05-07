namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Top-level decision policy. Given the current public game state, produce a
/// concrete action. Consumers swap implementations via DI — the live plugin
/// injects an <see cref="IPolicy"/>, never a concrete class.
///
/// Composing policies (efficiency, MCTS) decompose into sub-policy evaluators
/// (<see cref="IDiscardPolicy"/>, <see cref="ICallPolicy"/>,
/// <see cref="IRiichiPolicy"/>, ...) — Phase 4 wires that decomposition.
/// </summary>
public interface IPolicy
{
    ActionChoice Choose(StateSnapshot state);
}
