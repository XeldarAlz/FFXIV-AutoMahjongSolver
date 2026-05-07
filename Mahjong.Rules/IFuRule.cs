namespace Mahjong.Rules;

/// <summary>
/// Computes fu for a winning decomposition. Receives the already-detected yaku
/// list so it can spot Pinfu (flat fu) and Chiitoitsu (flat 25) without
/// re-deriving them.
/// </summary>
public interface IFuRule
{
    int Compute(Decomposition d, WinContext ctx, IReadOnlyList<YakuHit> yaku);
}
