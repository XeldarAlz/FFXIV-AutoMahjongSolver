using System;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.Logging;
using Serilog.Events;

namespace Mahjong.Plugin.Dalamud.Adapters;

/// <summary>Mirrors Warning/Error/Fatal events into the ErrorSink so they reach the errors telemetry stream; Info and below pass through only.</summary>
internal sealed class MirroredPluginLog : IPluginLog
{
    private readonly IPluginLog inner;
    private ErrorSink? sink;

    public MirroredPluginLog(IPluginLog inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    /// <summary>Sink is wired after DI is built; calls before attach pass through without mirroring.</summary>
    public void AttachSink(ErrorSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
    }

    public Serilog.ILogger Logger => inner.Logger;
    public LogEventLevel MinimumLogLevel
    {
        get => inner.MinimumLogLevel;
        set => inner.MinimumLogLevel = value;
    }

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

    public void Information(string messageTemplate, params object[] values) => inner.Information(messageTemplate, values);
    public void Information(Exception? exception, string messageTemplate, params object[] values) => inner.Information(exception, messageTemplate, values);

    public void Info(string messageTemplate, params object[] values) => inner.Info(messageTemplate, values);
    public void Info(Exception? exception, string messageTemplate, params object[] values) => inner.Info(exception, messageTemplate, values);

    public void Debug(string messageTemplate, params object[] values) => inner.Debug(messageTemplate, values);
    public void Debug(Exception? exception, string messageTemplate, params object[] values) => inner.Debug(exception, messageTemplate, values);

    public void Verbose(string messageTemplate, params object[] values) => inner.Verbose(messageTemplate, values);
    public void Verbose(Exception? exception, string messageTemplate, params object[] values) => inner.Verbose(exception, messageTemplate, values);

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
