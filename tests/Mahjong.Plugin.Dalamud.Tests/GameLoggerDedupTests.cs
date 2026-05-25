using Mahjong.Core;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Composition;
using Mahjong.Plugin.Dalamud.Logging;
using Mahjong.Plugin.Dalamud.Tests.Stubs;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Tests;

public class GameLoggerDedupTests
{
    private static readonly int[] StartScores = [25000, 25000, 25000, 25000];

    private static StateSnapshot SampleSnap(int wallRemaining, int handCount = 0, int[]? scores = null)
    {
        var seats = new SeatView[4];
        for (int i = 0; i < 4; i++)
            seats[i] = new SeatView(
                Discards: Array.Empty<Tile>(),
                DiscardIsTedashi: Array.Empty<bool>(),
                Melds: Array.Empty<Meld>(),
                Riichi: false,
                RiichiDiscardIndex: -1,
                Ippatsu: false,
                IsTenpaiCalled: false);
        var hand = new Tile[handCount];
        for (int i = 0; i < handCount; i++)
            hand[i] = Tile.FromId(i % Tile.Count34);
        return StateSnapshot.Empty with
        {
            WallRemaining = wallRemaining,
            Seats = seats,
            Hand = hand,
            Scores = scores ?? StartScores,
        };
    }

    [Fact]
    public void Identical_snapshots_collapse_to_a_single_state_line()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration());
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        var snap = SampleSnap(70);
        for (int i = 0; i < 1000; i++)
            logger.OnStateChanged(snap);

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"e\":\"hand-start\"", lines[0]);
        Assert.Contains("\"e\":\"state\"", lines[1]);
    }

    [Fact]
    public void Distinct_snapshots_emit_distinct_state_lines()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration());
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        for (int w = 70; w >= 66; w--)
            logger.OnStateChanged(SampleSnap(w));

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]);
        Assert.Equal(6, lines.Length);
    }

    // Regression: MaybeRollHand must reject upward wall jumps at mid-hand counts so transient reads don't produce truncated files.
    [Fact]
    public void Wall_jump_with_mid_hand_count_does_not_roll_new_file()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration());
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        logger.OnStateChanged(SampleSnap(70, handCount: 14));
        logger.OnStateChanged(SampleSnap(40, handCount: 14));
        logger.OnStateChanged(SampleSnap(34, handCount: 6));
        logger.OnStateChanged(SampleSnap(40, handCount: 6));
        logger.OnStateChanged(SampleSnap(70, handCount: 14));

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson");
        Assert.Equal(2, files.Length);
    }

    // Regression: hand-end for the prior hand is written into the new hand's file so it survives uploader mid-session moves.
    [Fact]
    public void Hand_roll_emits_hand_end_with_score_delta_into_new_file()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration());
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        logger.OnStateChanged(SampleSnap(70, handCount: 14, scores: [25000, 25000, 25000, 25000]));
        logger.OnStateChanged(SampleSnap(40, handCount: 14, scores: [25000, 25000, 25000, 25000]));
        logger.OnStateChanged(SampleSnap(70, handCount: 14, scores: [33000, 23000, 21000, 23000]));

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson").OrderBy(p => p).ToArray();
        Assert.Equal(2, files.Length);

        var hand1 = File.ReadAllLines(files[0]);
        Assert.Contains("\"e\":\"hand-start\"", hand1[0]);
        Assert.DoesNotContain(hand1, l => l.Contains("\"e\":\"hand-end\""));

        var hand2 = File.ReadAllLines(files[1]);
        Assert.Contains("\"e\":\"hand-end\"", hand2[0]);
        Assert.Contains("\"kind\":\"tsumo\"", hand2[0]);
        Assert.Contains("\"winner\":0", hand2[0]);
        Assert.Contains("\"deltas\":[8000,-2000,-4000,-2000]", hand2[0]);
        Assert.Contains("\"scores_after\":[33000,23000,21000,23000]", hand2[0]);
        Assert.Contains("\"e\":\"hand-start\"", hand2[1]);
    }

    // Regression: a deferred roll must not consume the wall-jump signal, so the next deal-shape tick still triggers the roll.
    [Fact]
    public void Hand_roll_defers_when_wall_jumps_but_hand_is_mid_deal()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration());
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        logger.OnStateChanged(SampleSnap(70, handCount: 14));
        logger.OnStateChanged(SampleSnap(40, handCount: 14));
        logger.OnStateChanged(SampleSnap(70, handCount: 7));
        logger.OnStateChanged(SampleSnap(70, handCount: 14));

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson");
        Assert.Equal(2, files.Length);
    }

    [Theory]
    [InlineData(new[] { 8000, -2000, -4000, -2000 }, "tsumo", 0, (object?)null)]
    [InlineData(new[] { 0, 5200, 0, -5200 }, "ron", 1, 3)]
    [InlineData(new[] { 1500, -1500, 1500, -1500 }, "draw", (object?)null, (object?)null)]
    [InlineData(new[] { 0, 0, 0, 0 }, "draw", (object?)null, (object?)null)]
    public void InferResultKind_classifies_delta_shapes(int[] deltas, string expectedKind, object? expectedWinner, object? expectedLoser)
    {
        var (kind, winner, loser) = GameLogger.InferResultKind(deltas);
        Assert.Equal(expectedKind, kind);
        Assert.Equal((int?)expectedWinner, winner);
        Assert.Equal((int?)expectedLoser, loser);
    }

    [Fact]
    public void EnableGameLogging_off_drops_all_writes()
    {
        using var tmp = new TempDir();
        var config = new DalamudConfigService(_ => { }, new Configuration { EnableGameLogging = false });
        using var logger = new GameLogger(config, new StubPluginLog(), tmp.Path);

        for (int i = 0; i < 50; i++)
            logger.OnStateChanged(SampleSnap(70 - i));

        var files = Directory.GetFiles(logger.GamesDir, "game-*.ndjson");
        Assert.Empty(files);
    }
}
