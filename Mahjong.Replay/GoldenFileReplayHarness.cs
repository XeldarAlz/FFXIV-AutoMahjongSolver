using System.IO;
using System.Text.Json;

namespace Mahjong.Replay;

/// <summary>
/// Runs a Tenhou kyoku through an <see cref="IPolicy"/> and compares the resulting
/// decision trace to a stored "golden" snapshot file. Used by the regression
/// test suite to catch policy drift between phases — if the new policy makes
/// different cuts on a recorded game, the golden-file diff fails the build.
///
/// Workflow:
/// <list type="bullet">
///   <item><see cref="Replay"/> runs the kyoku, builds a deterministic
///         <see cref="ReplaySnapshot"/>.</item>
///   <item><see cref="LoadGolden"/> reads the expected snapshot from disk
///         (returns null if missing).</item>
///   <item><see cref="WriteGolden"/> writes the snapshot — used both for
///         first-time generation and the explicit "regenerate baselines" mode.</item>
/// </list>
/// Tests use <see cref="VerifyOrUpdate"/> as the one-line entry point: in normal
/// mode it asserts equality; with <c>UPDATE_REPLAY_SNAPSHOTS=1</c> set it
/// regenerates the file.
/// </summary>
public static class GoldenFileReplayHarness
{
    private const string UpdateEnvVar = "UPDATE_REPLAY_SNAPSHOTS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Parse a Tenhou JSON file and replay its first kyoku at the given seat.</summary>
    public static ReplaySnapshot Replay(string tenhouJsonPath, IPolicy policy, int seat = 0)
    {
        ArgumentNullException.ThrowIfNull(tenhouJsonPath);
        ArgumentNullException.ThrowIfNull(policy);

        var json = File.ReadAllText(tenhouJsonPath);
        var kyokus = TenhouLog.ParseDocument(json);
        if (kyokus.Length == 0)
            throw new InvalidDataException($"Tenhou log {tenhouJsonPath} contains no kyokus");

        var result = TenhouReplay.ReplaySeat(kyokus[0], policy, seat);
        var entries = new ReplayDecisionEntry[result.Decisions.Length];
        for (int i = 0; i < result.Decisions.Length; i++)
        {
            var d = result.Decisions[i];
            entries[i] = new ReplayDecisionEntry(
                Turn: d.TurnIndex,
                Actual: d.ActualDiscard.ShortName,
                Policy: d.PolicyPick.ShortName,
                Matched: d.Matched);
        }

        return new ReplaySnapshot(
            Source: Path.GetFileName(tenhouJsonPath),
            Seat: seat,
            TotalDecisions: result.TotalDecisions,
            Matches: result.Matches,
            Accuracy: Math.Round(result.Accuracy, 4),
            Decisions: entries);
    }

    public static ReplaySnapshot? LoadGolden(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
            return null;
        var json = File.ReadAllText(snapshotPath);
        return JsonSerializer.Deserialize<ReplaySnapshot>(json, JsonOptions);
    }

    public static void WriteGolden(string snapshotPath, ReplaySnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var dir = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(snapshotPath, json);
    }

    /// <summary>
    /// Convenience entry point for tests. Replays the kyoku and either:
    /// <list type="bullet">
    ///   <item>Asserts equality with the golden file (normal mode), or</item>
    ///   <item>Writes/overwrites the golden file (when env var
    ///         <c>UPDATE_REPLAY_SNAPSHOTS=1</c> is set, or when no golden exists yet).</item>
    /// </list>
    /// </summary>
    public static GoldenFileResult VerifyOrUpdate(
        string tenhouJsonPath, string snapshotPath, IPolicy policy, int seat = 0)
    {
        var actual = Replay(tenhouJsonPath, policy, seat);
        bool updateMode = Environment.GetEnvironmentVariable(UpdateEnvVar) == "1";
        var existing = LoadGolden(snapshotPath);

        if (updateMode || existing is null)
        {
            WriteGolden(snapshotPath, actual);
            return new GoldenFileResult(
                Status: existing is null ? GoldenFileStatus.Created : GoldenFileStatus.Updated,
                Actual: actual,
                Expected: existing);
        }

        bool matches = ReplaySnapshot.Equal(existing, actual);
        return new GoldenFileResult(
            Status: matches ? GoldenFileStatus.Match : GoldenFileStatus.Mismatch,
            Actual: actual,
            Expected: existing);
    }
}

public enum GoldenFileStatus
{
    Match,
    Mismatch,
    Created,        // golden file didn't exist; was generated
    Updated,        // golden file existed and was overwritten (UPDATE_REPLAY_SNAPSHOTS=1)
}

public sealed record GoldenFileResult(
    GoldenFileStatus Status,
    ReplaySnapshot Actual,
    ReplaySnapshot? Expected);
