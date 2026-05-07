using System.IO;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Replay.Tests;

/// <summary>
/// Regression suite — replays every <c>*.tenhou.json</c> fixture in
/// <c>data/replays/</c> through <see cref="EfficiencyPolicy"/> with default
/// weights, and compares the resulting decision trace to the matching
/// <c>*.snapshot.json</c> golden file. Any drift in policy behavior shows
/// up as a test failure with a side-by-side diff.
///
/// To add a new fixture: drop a Tenhou-format log into <c>data/replays/</c>;
/// run the suite once with <c>UPDATE_REPLAY_SNAPSHOTS=1</c> to generate the
/// snapshot; commit both files together.
///
/// To accept new behavior as the baseline: re-run with the env var set;
/// review and commit the updated snapshot.
/// </summary>
public class GoldenFileTests
{
    public static IEnumerable<object[]> ReplayFixtures()
    {
        var dir = RepoPathResolver.Resolve("data", "replays");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(dir, "*.tenhou.json"))
            yield return new object[] { Path.GetFileName(path) };
    }

    [Theory]
    [MemberData(nameof(ReplayFixtures))]
    public void Replay_matches_golden_file(string fixtureName)
    {
        var dir = RepoPathResolver.Resolve("data", "replays");
        var tenhouPath = Path.Combine(dir, fixtureName);
        var snapshotPath = Path.Combine(
            dir, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fixtureName)) + ".snapshot.json");

        var policy = new EfficiencyPolicy();
        var result = GoldenFileReplayHarness.VerifyOrUpdate(tenhouPath, snapshotPath, policy, seat: 0);

        switch (result.Status)
        {
            case GoldenFileStatus.Match:
                // Behavior matches the committed baseline.
                break;
            case GoldenFileStatus.Created:
                // First-time fixture: snapshot was just generated. Pass — but
                // surface the message so the developer knows to commit the file.
                Assert.NotNull(result.Actual);
                break;
            case GoldenFileStatus.Updated:
                // UPDATE_REPLAY_SNAPSHOTS=1 was set — pass and let the developer
                // review the diff before committing.
                Assert.NotNull(result.Actual);
                break;
            case GoldenFileStatus.Mismatch:
                Assert.Fail(
                    $"Replay drift detected for {fixtureName}.\n" +
                    $"  expected: matches={result.Expected!.Matches}/{result.Expected.TotalDecisions} " +
                    $"acc={result.Expected.Accuracy:F4}\n" +
                    $"  actual:   matches={result.Actual.Matches}/{result.Actual.TotalDecisions} " +
                    $"acc={result.Actual.Accuracy:F4}\n" +
                    $"Set UPDATE_REPLAY_SNAPSHOTS=1 and re-run to accept the new baseline.");
                break;
        }
    }
}
