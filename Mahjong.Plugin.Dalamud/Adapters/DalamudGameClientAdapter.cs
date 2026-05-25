namespace Mahjong.Plugin.Dalamud.Adapters;

internal sealed class DalamudGameClientAdapter : IGameClientAdapter
{
    public IFrameworkScheduler Scheduler { get; }
    public IEventLog Log { get; }

    public DalamudGameClientAdapter(IFrameworkScheduler scheduler, IEventLog log)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(log);
        Scheduler = scheduler;
        Log = log;
    }
}
