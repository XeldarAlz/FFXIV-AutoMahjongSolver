namespace Mahjong.Rules;

public static class TileIds
{
    public const int ManStart = 0;
    public const int PinStart = 9;
    public const int SouStart = 18;
    public const int HonorStart = 27;
    public const int SuitSize = 9;

    public const int EastWind = 27;
    public const int SouthWind = 28;
    public const int WestWind = 29;
    public const int NorthWind = 30;
    public const int Haku = 31;
    public const int Hatsu = 32;
    public const int Chun = 33;

    public const int FirstWind = EastWind;
    public const int LastWind = NorthWind;
    public const int FirstDragon = Haku;
    public const int LastDragon = Chun;
}

public static class ScoringConstants
{
    public const int ManganBase = 2000;
    public const int HanemanBase = 3000;
    public const int BaimanBase = 4000;
    public const int SanbaimanBase = 6000;
    public const int YakumanBase = 8000;

    public const int ManganHan = 5;
    public const int HanemanHan = 6;
    public const int BaimanHan = 8;
    public const int SanbaimanHan = 11;
    public const int KazoeYakumanHan = 13;

    public const int PaymentRoundStep = 100;
}

public static class HanValues
{
    public const int Yakuman = 13;
    public const int DoubleYakuman = 26;
}
