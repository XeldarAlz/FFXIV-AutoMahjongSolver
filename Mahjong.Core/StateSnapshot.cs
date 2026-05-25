namespace Mahjong.Core;

/// <summary>
/// Public state of one seat. Closed hands of opponents are NOT here — they belong in the
/// opponent-model belief state. <paramref name="DiscardCount"/> is authoritative when the
/// tile-pool resolution fails and <paramref name="Discards"/> ends up empty; prefer it over
/// <c>Discards.Count</c> in policy code.
/// </summary>
public sealed record SeatView(
    IReadOnlyList<Tile> Discards,
    IReadOnlyList<bool> DiscardIsTedashi,
    IReadOnlyList<Meld> Melds,
    bool Riichi,
    int RiichiDiscardIndex,
    bool Ippatsu,
    bool IsTenpaiCalled,
    int DiscardCount = 0)
{
    public IReadOnlyList<Tile> Discards { get; init; } = [.. Discards];
    public IReadOnlyList<bool> DiscardIsTedashi { get; init; } = [.. DiscardIsTedashi];
    public IReadOnlyList<Meld> Melds { get; init; } = [.. Melds];
}

/// <summary>
/// Immutable table state from our perspective. <see cref="SchemaVersion"/> is bumped on any
/// shape change; the aggregator rejects mismatched snapshots. Seats: 0=E, 1=S, 2=W, 3=N.
/// </summary>
/// <param name="SeatInfoKnown">
/// False when OurSeat/RoundWind are defaults — yakuhai-on-winds must gate on this since an
/// unconfirmed seat wind biases the policy toward keeping useless winds.
/// </param>
/// <param name="AkaDora">
/// Red-5 count in the closed hand. Side-channel so Tile stays 1-byte; Scorer adds to dora.
/// </param>
/// <param name="AddonStateCode">
/// Raw addon state code (-1 = unknown). Disambiguates dispatch contexts the Legal enum can't —
/// e.g. state-6 self-declare popup vs. state-30 classic discard, both Legal=Discard.
/// </param>
public sealed record StateSnapshot(
    IReadOnlyList<Tile> Hand,
    IReadOnlyList<Meld> OurMelds,
    int OurSeat,
    bool OurRiichi,
    bool OurIppatsu,
    bool OurDoubleRiichi,
    int RoundWind,
    int Honba,
    int RiichiSticks,
    IReadOnlyList<int> Scores,
    IReadOnlyList<Tile> DoraIndicators,
    IReadOnlyList<Tile> UraDoraIndicators,
    int WallRemaining,
    int TurnIndex,
    int DealerSeat,
    IReadOnlyList<SeatView> Seats,
    LegalActions Legal,
    int SchemaVersion,
    bool SeatInfoKnown = false,
    int AkaDora = 0,
    int AddonStateCode = -1)
{
    public const int CurrentSchemaVersion = 4;

    public IReadOnlyList<Tile> Hand { get; init; } = [.. Hand];
    public IReadOnlyList<Meld> OurMelds { get; init; } = [.. OurMelds];
    public IReadOnlyList<int> Scores { get; init; } = [.. Scores];
    public IReadOnlyList<Tile> DoraIndicators { get; init; } = [.. DoraIndicators];
    public IReadOnlyList<Tile> UraDoraIndicators { get; init; } = [.. UraDoraIndicators];
    public IReadOnlyList<SeatView> Seats { get; init; } = [.. Seats];

    public SeatView Us => Seats[OurSeat];

    public static StateSnapshot Empty { get; } = new(
        Hand: [],
        OurMelds: [],
        OurSeat: 0,
        OurRiichi: false,
        OurIppatsu: false,
        OurDoubleRiichi: false,
        RoundWind: 0,
        Honba: 0,
        RiichiSticks: 0,
        Scores: [25000, 25000, 25000, 25000],
        DoraIndicators: [],
        UraDoraIndicators: [],
        WallRemaining: 70,
        TurnIndex: 0,
        DealerSeat: 0,
        Seats: [EmptySeat(), EmptySeat(), EmptySeat(), EmptySeat()],
        Legal: LegalActions.None,
        SchemaVersion: CurrentSchemaVersion);

    private static SeatView EmptySeat() => new(
        Discards: [],
        DiscardIsTedashi: [],
        Melds: [],
        Riichi: false,
        RiichiDiscardIndex: -1,
        Ippatsu: false,
        IsTenpaiCalled: false);
}
