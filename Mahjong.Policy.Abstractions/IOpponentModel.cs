namespace Mahjong.Policy.Abstractions;

/// <summary>Mutable; <see cref="Update"/> recomputes from scratch each call.</summary>
public interface IOpponentModel
{
    int OpponentCount { get; }

    void Update(StateSnapshot state);

    /// <summary>Index relative to self: 0=shimocha, 1=toimen, 2=kamicha.</summary>
    double TenpaiProbability(int opponentIndex);

    /// <summary>Sum of P(deal-in) × expected hand value across opponents.</summary>
    double ExpectedDealInCost(int tileId);
}
