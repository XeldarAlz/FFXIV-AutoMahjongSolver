using Dalamud.Plugin.Services;

namespace Mahjong.Plugin.Dalamud.Adapters;

internal sealed class DalamudEventLog : IEventLog
{
    private readonly IPluginLog log;

    public DalamudEventLog(IPluginLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        this.log = log;
    }

    public void Log(EventLevel level, string category, string message, Exception? exception = null)
    {
        var line = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
        switch (level)
        {
            case EventLevel.Info:
                log.Information(line);
                break;
            case EventLevel.Warning:
                log.Warning(line);
                break;
            case EventLevel.Error when exception is null:
                log.Error(line);
                break;
            case EventLevel.Error:
                log.Error(exception, line);
                break;
        }
    }
}
