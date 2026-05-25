namespace Mahjong.Plugin.Game.Variants;

/// <summary>
/// Variant constants loaded from <c>data/layouts/*.json</c>. Adding a new variant is one JSON
/// file — never a code change.
/// </summary>
public sealed record LayoutProfile(
    string Name,
    string AddonName,
    int TileTextureBase,
    LayoutOffsets Offsets,
    LayoutNodeIds NodeIds,
    LayoutAtkValueIndices AtkValues,
    LayoutStateCodes StateCodes,
    LayoutSanityLimits Limits);

/// <summary>
/// Seat stride ~0x2E0. Per-seat discard array offsets are optional — null leaves
/// <see cref="Mahjong.Core.SeatView.Discards"/> empty.
/// </summary>
public sealed record LayoutOffsets(
    int SelfScore,
    int ShimochaScore,
    int ToimenScore,
    int KamichaScore,
    int SelfDiscardCountByte,
    int ShimochaDiscardCountByte,
    int ToimenDiscardCountByte,
    int KamichaDiscardCountByte,
    int HandArrayStart,
    int DoraIndicator,
    int? SelfDiscardArray = null,
    int? ShimochaDiscardArray = null,
    int? ToimenDiscardArray = null,
    int? KamichaDiscardArray = null,
    int DiscardArrayMaxLen = 24);

public sealed record LayoutNodeIds(
    uint CallModalHost,
    uint CallModalShell);

/// <summary>Scan-window fields are per-variant because EmjL places claim slots differently than Emj.</summary>
public sealed record LayoutAtkValueIndices(
    int StateCode,
    int WallCount,
    int ChiClaimedTile,
    int PonClaimScanLo = 16,
    int PonClaimScanHi = 21,
    int ChiFallbackScanLimit = 30,
    int ButtonLabelScanLimit = 20);

public sealed record LayoutStateCodes(
    int OurTurnDiscard,
    int CallPrompt,
    int CallPromptList,
    int SelfDeclareList,
    int PostDrawIdle);

public sealed record LayoutSanityLimits(
    int HandSize,
    int WallInitial,
    int ScoreSanityMax,
    int DiscardCountSanityMax,
    int MaxAkadoraSlots);
