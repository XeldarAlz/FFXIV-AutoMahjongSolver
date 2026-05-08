using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Telemetry;

/// <summary>
/// Flat append-only NDJSON log of every discard observed across all matches —
/// distinct from <see cref="Logging.GameLogger"/>'s per-hand snapshots, and
/// distinct from <see cref="Logging.DiscardCaptureLogger"/>'s diagnostic text
/// log. This file is what the <c>discards</c> telemetry stream ships, giving
/// the offline corpus a long-running flat record of every tile played for
/// pattern/ML analysis.
///
/// <para>One line per <see cref="IDiscardCapture.DiscardObserved"/> event;
/// daily roll under <c>pluginConfigs/&lt;plugin&gt;/discards/discards-yyyyMMdd.ndjson</c>.
/// IO failures are swallowed — telemetry must never break the capture
/// pipeline.</para>
/// </summary>
public sealed class DiscardTracker : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDiscardCapture capture;
    private readonly string discardsDir;
    private readonly object writerLock = new();
    private long sequence;
    private bool disposed;

    public string DiscardsDir => discardsDir;

    public DiscardTracker(IDiscardCapture capture, string pluginConfigDirectory)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDirectory);
        this.capture = capture;
        discardsDir = Path.Combine(pluginConfigDirectory, "discards");
        try
        { Directory.CreateDirectory(discardsDir); }
        catch { }

        capture.DiscardObserved += OnDiscard;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        capture.DiscardObserved -= OnDiscard;
    }

    private void OnDiscard(DiscardEvent evt)
    {
        if (disposed)
            return;
        try
        {
            var entry = new DiscardEntry(
                T: evt.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                Seq: Interlocked.Increment(ref sequence),
                ObsSeq: evt.SequenceNumber,
                Strategy: capture.StrategyName,
                Seat: evt.Seat,
                TileId: evt.Tile.Id,
                Tile: evt.Tile.ToString());
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            var path = Path.Combine(discardsDir, $"discards-{DateTime.UtcNow:yyyyMMdd}.ndjson");
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

    private sealed record DiscardEntry(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("obs_seq")] ulong ObsSeq,
        [property: JsonPropertyName("strategy")] string Strategy,
        [property: JsonPropertyName("seat")] int Seat,
        [property: JsonPropertyName("tile_id")] int TileId,
        [property: JsonPropertyName("tile")] string Tile);
}
