using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.Telemetry;

/// <summary>
/// Append-only NDJSON log of every FireCallback dispatched to the Mahjong
/// addon: one line per click on the in-game UI (discard, pon, chi, kan,
/// riichi, pass, etc.). The addon's int values capture the action opcode
/// plus its option index, so this stream is what downstream ML / input
/// replay tooling consumes for training.
///
/// <para>Distinct from the diagnostic <c>emj-events.log</c> that
/// <see cref="InputEventLogger"/> writes when its <c>Enabled</c> flag is on:
/// that file is verbose RE text. This one is structured NDJSON shipped via
/// the <c>inputs</c> telemetry stream, daily roll under
/// <c>pluginConfigs/&lt;plugin&gt;/inputs/inputs-yyyyMMdd.ndjson</c>.</para>
///
/// <para>Subscribes to <see cref="InputEventLogger.CallbackObserved"/>, which
/// fires after the original game callback runs and is filtered to Mahjong
/// addons. Always-on regardless of the diagnostic flag. IO failures are
/// swallowed — telemetry must never break the input pipeline.</para>
/// </summary>
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
            // Never throw from a logger.
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
