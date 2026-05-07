namespace Mahjong.Policy.Abstractions;

/// <summary>
/// Per-opponent belief state — tenpai probability, hand marginals, deal-in
/// risk. Discard scorer and rollout consume this to weight defensive play.
///
/// Implementations are mutable: <see cref="Update"/> recomputes everything
/// from the latest snapshot. The model holds no cross-tick state — it's
/// safe to <see cref="Update"/> on every decision point.
/// </summary>
public interface IOpponentModel
{
    /// <summary>How many opponents this model tracks (always 3 in 4-player mahjong).</summary>
    int OpponentCount { get; }

    /// <summary>Recompute every per-opponent estimate from the snapshot.</summary>
    void Update(StateSnapshot state);

    /// <summary>Probability the given opponent is tenpai. Index relative to self (0=shimocha, 1=toimen, 2=kamicha).</summary>
    double TenpaiProbability(int opponentIndex);

    /// <summary>Sum of P(deal-in) × expected hand value across all opponents if we discard this tile.</summary>
    double ExpectedDealInCost(int tileId);
}
