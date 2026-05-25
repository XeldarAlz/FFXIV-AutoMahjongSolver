using Dalamud.Plugin.Services;

namespace Mahjong.Plugin.Dalamud.Adapters;

internal sealed class DalamudFrameworkScheduler : IFrameworkScheduler
{
    private readonly IFramework framework;

    public DalamudFrameworkScheduler(IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(framework);
        this.framework = framework;
    }

    public bool IsOnFrameworkThread => framework.IsInFrameworkUpdateThread;

    public Task RunOnFrameworkThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return framework.RunOnFrameworkThread(action);
    }

    public Task RunOnTickAsync(TimeSpan delay, Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return framework.RunOnTick(action, delay, cancellationToken: cancellationToken);
    }
}
