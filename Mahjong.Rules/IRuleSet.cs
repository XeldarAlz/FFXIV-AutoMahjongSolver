namespace Mahjong.Rules;

public interface IRuleSet
{
    string Name { get; }

    IReadOnlyList<IYakuRule> YakuRules { get; }

    IScoringRule ScoringRule { get; }
    IDoraRule DoraRule { get; }
    IFuRule FuRule { get; }

    bool AllowsRedDora { get; }

    /// <summary>True keeps the tanyao yaku on opened hands (riichi default).</summary>
    bool AllowsKuitan { get; }

    /// <summary>Minimum han for a declarable win. Riichi: 1. Doman: 2.</summary>
    int MinHan { get; }

    /// <summary>Han threshold at which a non-yakuman becomes counted yakuman (riichi: 13).</summary>
    int KazoeThreshold { get; }

    int MaxYakuman { get; }
}
