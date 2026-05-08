using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Mahjong.Plugin.Dalamud.Telemetry;

/// <summary>
/// Append-only NDJSON log of every signature-scan attempt the plugin makes
/// against the live FFXIV process. One line per probe with pattern, match
/// address (or null on miss), and elapsed time. Lets us correlate sigscan
/// failures across game patches without relying on every probe site
/// remembering to call <see cref="Logging.IFindingsLog"/>.
///
/// <para>Files land under
/// <c>pluginConfigs/&lt;plugin&gt;/sigprobes/sigprobes-yyyyMMdd.ndjson</c>;
/// <see cref="TelemetryUploader"/> picks them up via the <c>sigprobes</c>
/// stream. Daily roll mirrors <see cref="Logging.FindingsLog"/>.</para>
/// </summary>
public interface ISigprobeLog
{
    void Record(string sigName, string pattern, nint matchAddress, double elapsedMs, bool success, string? errorMessage = null);
}

/// <summary>Inert no-op used in tests and code paths that don't ship telemetry.</summary>
public sealed class NullSigprobeLog : ISigprobeLog
{
    public static readonly NullSigprobeLog Instance = new();
    public void Record(string sigName, string pattern, nint matchAddress, double elapsedMs, bool success, string? errorMessage = null) { }
}

public sealed class SigprobeLog : ISigprobeLog
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string sigprobesDir;
    private readonly object writerLock = new();
    private long sequence;

    public string SigprobesDir => sigprobesDir;

    public SigprobeLog(string pluginConfigDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDirectory);
        sigprobesDir = Path.Combine(pluginConfigDirectory, "sigprobes");
        try
        { Directory.CreateDirectory(sigprobesDir); }
        catch { }
    }

    public void Record(string sigName, string pattern, nint matchAddress, double elapsedMs, bool success, string? errorMessage = null)
    {
        if (string.IsNullOrEmpty(sigName))
            return;
        try
        {
            var entry = new SigprobeEntry(
                T: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Seq: Interlocked.Increment(ref sequence),
                Name: sigName,
                Pattern: pattern ?? "",
                Address: success && matchAddress != 0 ? $"0x{matchAddress:X}" : null,
                ElapsedMs: Math.Round(elapsedMs, 3),
                Success: success,
                Error: errorMessage);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            var path = Path.Combine(sigprobesDir, $"sigprobes-{DateTime.UtcNow:yyyyMMdd}.ndjson");
            lock (writerLock)
            {
                using var w = new StreamWriter(new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.Read));
                w.WriteLine(line);
            }
        }
        catch
        {
            // Never throw from a logger.
        }
    }

    private sealed record SigprobeEntry(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("pattern")] string Pattern,
        [property: JsonPropertyName("addr")] string? Address,
        [property: JsonPropertyName("elapsed_ms")] double ElapsedMs,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error);
}
