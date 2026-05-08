using System;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.Logging;
using Serilog.Events;

namespace Mahjong.Plugin.Dalamud.Adapters;

/// <summary>
/// <see cref="IPluginLog"/> wrapper that forwards every call through to the
/// Dalamud-injected logger and additionally mirrors Warning / Error / Fatal
/// events into the plugin's <see cref="ErrorSink"/> so they land in the
/// <c>errors</c> telemetry stream.
///
/// <para>Motivation: warnings emitted via <c>Plugin.Log.Warning(...)</c> only
/// show up in Dalamud's local plugin log. Without this proxy they're invisible
/// to corpus analysis — surfaced only when a user pastes their log file. The
/// 2026-05-09 file-handle deadlock between <see cref="Telemetry.TelemetryUploader"/>
/// and <see cref="GameLogger"/> was the canonical missed case: the IOException
/// fired every 60 seconds for hours but never reached the corpus.</para>
///
/// <para><b>Late-bind sink:</b> <see cref="ErrorSink"/> is constructed in
/// Plugin.cs <em>after</em> the DI container is built, so the sink reference
/// gets attached lazily via <see cref="AttachSink"/>. Calls before the attach
/// (very early plugin startup) just pass through to the inner logger, no
/// mirroring — that period is short and only logs Dalamud's own load-time
/// chatter, which we don't need in our corpus anyway.</para>
///
/// <para>Info / Debug / Verbose are never mirrored — the errors stream is for
/// problems, not chatter. Filter at the source, not at corpus-analysis time.</para>
/// </summary>
internal sealed class MirroredPluginLog : IPluginLog
{
    private readonly IPluginLog inner;
    private ErrorSink? sink;

    public MirroredPluginLog(IPluginLog inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    /// <summary>
    /// Wire up the error sink once Plugin.cs has constructed it. Idempotent
    /// — calling twice replaces the prior sink (useful for tests).
    /// </summary>
    public void AttachSink(ErrorSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
    }

    // ---- Properties (forward to inner) ---------------------------------
    public Serilog.ILogger Logger => inner.Logger;
    public LogEventLevel MinimumLogLevel
    {
        get => inner.MinimumLogLevel;
        set => inner.MinimumLogLevel = value;
    }

    // ---- Fatal ----------------------------------------------------------
    public void Fatal(string messageTemplate, params object[] values)
    {
        inner.Fatal(messageTemplate, values);
        sink?.RecordWarning("PluginLog.Fatal", SafeFormat(messageTemplate, values));
    }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values)
    {
        inner.Fatal(exception, messageTemplate, values);
        if (exception is not null)
            sink?.RecordException("PluginLog.Fatal", exception);
        else
            sink?.RecordWarning("PluginLog.Fatal", SafeFormat(messageTemplate, values));
    }

    // ---- Error ----------------------------------------------------------
    public void Error(string messageTemplate, params object[] values)
    {
        inner.Error(messageTemplate, values);
        sink?.RecordWarning("PluginLog.Error", SafeFormat(messageTemplate, values));
    }

    public void Error(Exception? exception, string messageTemplate, params object[] values)
    {
        inner.Error(exception, messageTemplate, values);
        if (exception is not null)
            sink?.RecordException("PluginLog.Error", exception);
        else
            sink?.RecordWarning("PluginLog.Error", SafeFormat(messageTemplate, values));
    }

    // ---- Warning --------------------------------------------------------
    public void Warning(string messageTemplate, params object[] values)
    {
        inner.Warning(messageTemplate, values);
        sink?.RecordWarning("PluginLog.Warning", SafeFormat(messageTemplate, values));
    }

    public void Warning(Exception? exception, string messageTemplate, params object[] values)
    {
        inner.Warning(exception, messageTemplate, values);
        if (exception is not null)
            sink?.RecordException("PluginLog.Warning", exception);
        else
            sink?.RecordWarning("PluginLog.Warning", SafeFormat(messageTemplate, values));
    }

    // ---- Info / Information / Debug / Verbose: forward only ------------
    public void Information(string messageTemplate, params object[] values) => inner.Information(messageTemplate, values);
    public void Information(Exception? exception, string messageTemplate, params object[] values) => inner.Information(exception, messageTemplate, values);

    public void Info(string messageTemplate, params object[] values) => inner.Info(messageTemplate, values);
    public void Info(Exception? exception, string messageTemplate, params object[] values) => inner.Info(exception, messageTemplate, values);

    public void Debug(string messageTemplate, params object[] values) => inner.Debug(messageTemplate, values);
    public void Debug(Exception? exception, string messageTemplate, params object[] values) => inner.Debug(exception, messageTemplate, values);

    public void Verbose(string messageTemplate, params object[] values) => inner.Verbose(messageTemplate, values);
    public void Verbose(Exception? exception, string messageTemplate, params object[] values) => inner.Verbose(exception, messageTemplate, values);

    // ---- Write (level-explicit, also gets mirrored at Warning+) --------
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
    {
        inner.Write(level, exception, messageTemplate, values);
        if (level < LogEventLevel.Warning)
            return;
        if (exception is not null)
            sink?.RecordException($"PluginLog.{level}", exception);
        else
            sink?.RecordWarning($"PluginLog.{level}", SafeFormat(messageTemplate, values));
    }

    /// <summary>
    /// Print-safe rendering of a Serilog template + values for the corpus.
    /// We don't try to interpolate the template's named placeholders — that's
    /// Serilog's job and the Dalamud log already has the formatted form. For
    /// our errors stream, "<template> | values=[v0, v1, ...]" is grep-friendly
    /// and never throws.
    /// </summary>
    private static string SafeFormat(string template, object[]? values)
    {
        var t = template ?? "";
        if (values is null || values.Length == 0)
            return t;
        try
        { return $"{t} | values=[{string.Join(", ", values)}]"; }
        catch
        { return t; }
    }
}
