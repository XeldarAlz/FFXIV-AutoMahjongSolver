namespace Mahjong.Rules;

public interface IScoringRule
{
    /// <summary>Yakuman hands carry their multiplier in han (13 = single, 26 = double, ...).</summary>
    ScoringTier ResolveTier(int han, int fu, bool isYakuman);

    Payments Pay(ScoringTier tier, bool isDealer, WinKind kind);
}

public sealed record ScoringTier(string Name, int BasePoints);
