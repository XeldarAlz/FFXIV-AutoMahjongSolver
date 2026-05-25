using System.IO;
using System.Text.Json;

namespace Mahjong.Replay;

/// <summary>
/// Use <see cref="VerifyOrUpdate"/> as the entry point. Setting env var
/// <c>UPDATE_REPLAY_SNAPSHOTS=1</c> regenerates golden files instead of asserting.
/// </summary>
public static class GoldenFileReplayHarness
{
    private const string UpdateEnvVar = "UPDATE_REPLAY_SNAPSHOTS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

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
    Created,
    Updated,
}

public sealed record GoldenFileResult(
    GoldenFileStatus Status,
    ReplaySnapshot Actual,
    ReplaySnapshot? Expected);
