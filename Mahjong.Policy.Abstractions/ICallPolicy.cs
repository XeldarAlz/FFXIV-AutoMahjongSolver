namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Decides whether to claim an offered call (pon / chi / kan). The Decision's
/// payload is the selected candidate to claim; null means "pass on all of them."
/// </summary>
public interface ICallPolicy
{
    Decision<MeldCandidate?> Evaluate(StateSnapshot state);
}
