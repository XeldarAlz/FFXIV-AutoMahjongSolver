namespace Mahjong.Plugin.Game;

public interface IFrameworkScheduler
{
    bool IsOnFrameworkThread { get; }

    Task RunOnFrameworkThreadAsync(Action action, CancellationToken cancellationToken = default);

    Task RunOnTickAsync(TimeSpan delay, Action action, CancellationToken cancellationToken = default);
}
