namespace Mahjong.Plugin.Game;

public interface IGameClientAdapter
{
    IFrameworkScheduler Scheduler { get; }
    IEventLog Log { get; }
}
