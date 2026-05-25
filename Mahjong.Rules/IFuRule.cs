namespace Mahjong.Rules;

/// <summary>
/// Receives detected yaku so it can apply Pinfu (flat fu) and Chiitoitsu (flat 25)
/// without re-deriving them.
/// </summary>
public interface IFuRule
{
    int Compute(Decomposition d, WinContext ctx, IReadOnlyList<YakuHit> yaku);
}
