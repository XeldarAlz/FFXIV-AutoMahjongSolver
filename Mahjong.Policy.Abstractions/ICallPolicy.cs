namespace Mahjong.Policy.Abstractions;

/// <summary>Decision payload null = pass on all candidates.</summary>
public interface ICallPolicy
{
    Decision<MeldCandidate?> Evaluate(StateSnapshot state);
}
