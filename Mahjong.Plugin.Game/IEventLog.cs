namespace Mahjong.Plugin.Game;

public enum EventLevel
{
    Info,
    Warning,
    Error,
}

public interface IEventLog
{
    void Log(EventLevel level, string category, string message, Exception? exception = null);

    void Info(string category, string message) => Log(EventLevel.Info, category, message);
    void Warn(string category, string message) => Log(EventLevel.Warning, category, message);
    void Error(string category, string message, Exception? ex = null) => Log(EventLevel.Error, category, message, ex);
}
