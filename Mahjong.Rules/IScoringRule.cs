namespace Mahjong.Rules;

/// <summary>
/// Resolves the (han, fu) → base-points → payments pipeline.
/// Implementations encode a specific scoring table (standard riichi-style for now).
/// </summary>
public interface IScoringRule
{
    /// <summary>
    /// Map (han, fu) into a tier. Yakuman hands carry their multiplier in the han
    /// (13 = single yakuman, 26 = double yakuman, etc.).
    /// </summary>
    ScoringTier ResolveTier(int han, int fu, bool isYakuman);

    /// <summary>Final payment breakdown given a tier, dealer flag, and win kind.</summary>
    Payments Pay(ScoringTier tier, bool isDealer, WinKind kind);
}

/// <summary>
/// One bucket of the scoring table — a name (for display) plus the base-points
/// multiplier that subsequent payment math uses.
/// </summary>
public sealed record ScoringTier(string Name, int BasePoints);
