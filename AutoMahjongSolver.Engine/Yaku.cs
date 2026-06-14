namespace AutoMahjongSolver.Engine;

/// <summary>The riichi yaku set recognized by Doman Mahjong.</summary>
public enum Yaku
{
    // 1 han
    Riichi,
    Ippatsu,
    MenzenTsumo,
    Pinfu,
    Tanyao,
    Iipeiko,
    SeatWind,
    RoundWind,
    Haku,
    Hatsu,
    Chun,
    Haitei,
    Houtei,
    Rinshan,
    Chankan,

    // 2 han (some drop to 1 when open)
    DoubleRiichi,
    Chiitoitsu,
    SanshokuDoujun,
    SanshokuDoukou,
    Ittsu,
    Chanta,
    Toitoi,
    Sanankou,
    Sankantsu,
    Shousangen,
    Honroutou,

    // 3+ han
    Junchan,
    Honitsu,
    Ryanpeiko,
    Chinitsu,

    // yakuman
    KokushiMusou,
    Suuankou,
    Daisangen,
    Shousuushii,
    Daisuushii,
    Tsuuiisou,
    Chinroutou,
    Ryuuiisou,
    ChuurenPoutou,
    Suukantsu,
}

/// <summary>A detected yaku and the han it contributes (open/closed already applied).</summary>
public readonly record struct YakuHan(Yaku Yaku, int Han);
