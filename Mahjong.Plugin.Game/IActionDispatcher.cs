namespace Mahjong.Plugin.Game;

public enum DispatchResult
{
    Ok,
    AddonNotFound,
    AddonNotVisible,
    InvalidSlot,
    HookFailed,
}

/// <summary>Implementations own Dalamud thread-discipline; caller may be on any thread.</summary>
public interface IActionDispatcher
{
    Task<DispatchResult> DispatchAsync(ActionChoice action, CancellationToken cancellationToken = default);
}
