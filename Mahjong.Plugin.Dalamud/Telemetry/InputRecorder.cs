using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed class InputRecorder : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly InputEventLogger logger;
    private readonly string inputsDir;
    private readonly object writerLock = new();
    private long sequence;
    private bool disposed;

    public string InputsDir => inputsDir;

    public InputRecorder(InputEventLogger logger, string pluginConfigDirectory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDirectory);
        this.logger = logger;
        inputsDir = Path.Combine(pluginConfigDirectory, "inputs");
        try
        { Directory.CreateDirectory(inputsDir); }
        catch { }

        logger.CallbackObserved += OnCallback;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        logger.CallbackObserved -= OnCallback;
    }

    private void OnCallback(InputCallbackEvent evt)
    {
        if (disposed)
            return;
        try
        {
            var entry = new InputEntry(
                T: evt.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                Seq: Interlocked.Increment(ref sequence),
                Addon: evt.AddonName,
                ValueCount: evt.ValueCount,
                Close: evt.Close,
                Result: evt.Result,
                Values: evt.IntValues);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            var path = Path.Combine(inputsDir, $"inputs-{DateTime.UtcNow:yyyyMMdd}.ndjson");
            lock (writerLock)
            {
                using var w = new StreamWriter(new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.Read));
                w.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    private sealed record InputEntry(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("addon")] string Addon,
        [property: JsonPropertyName("count")] uint ValueCount,
        [property: JsonPropertyName("close")] bool Close,
        [property: JsonPropertyName("result")] bool Result,
        [property: JsonPropertyName("values")] int?[] Values);
}
