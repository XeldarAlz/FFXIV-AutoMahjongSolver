namespace Mahjong.Rules;

/// <summary>
/// Centralized registry for every magic number that drives mahjong rules.
/// If a number appears in scoring, fu, or yaku detection logic, it lives here
/// — nowhere else.
/// </summary>
public static class TileIds
{
    // Suit boundaries inside the 34-space tile id layout.
    public const int ManStart = 0;
    public const int PinStart = 9;
    public const int SouStart = 18;
    public const int HonorStart = 27;
    public const int SuitSize = 9;

    // Specific honor ids.
    public const int EastWind = 27;
    public const int SouthWind = 28;
    public const int WestWind = 29;
    public const int NorthWind = 30;
    public const int Haku = 31;   // white dragon
    public const int Hatsu = 32;  // green dragon
    public const int Chun = 33;   // red dragon

    public const int FirstWind = EastWind;
    public const int LastWind = NorthWind;
    public const int FirstDragon = Haku;
    public const int LastDragon = Chun;
}

/// <summary>
/// Han thresholds and base-point tiers used by the standard riichi/Doman scoring table.
/// </summary>
public static class ScoringConstants
{
    /// <summary>Base points capped at this for any non-yakuman hand.</summary>
    public const int ManganBase = 2000;
    public const int HanemanBase = 3000;
    public const int BaimanBase = 4000;
    public const int SanbaimanBase = 6000;
    public const int YakumanBase = 8000;

    /// <summary>Han at and above which a hand jumps to a fixed tier.</summary>
    public const int ManganHan = 5;
    public const int HanemanHan = 6;
    public const int BaimanHan = 8;
    public const int SanbaimanHan = 11;
    public const int KazoeYakumanHan = 13;

    /// <summary>Round-up step for final payments.</summary>
    public const int PaymentRoundStep = 100;
}

/// <summary>
/// Han values used by the standard rule definitions.
/// </summary>
public static class HanValues
{
    public const int Yakuman = 13;
    public const int DoubleYakuman = 26;
}
