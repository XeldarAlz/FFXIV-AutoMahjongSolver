using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Shared service bundle for every dev-console tab.</summary>
internal sealed class DevConsoleContext
{
    public Plugin Plugin { get; }
    public IFramework Framework { get; }
    public ICommandManager CommandManager { get; }
    public MahjongAddon Addon { get; }

    /// <summary>Pinned status line under the active tab; tabs write, <see cref="DebugOverlay"/> renders.</summary>
    public string LastToast { get; set; } = "";

    public DevConsoleContext(
        Plugin plugin, IFramework framework, ICommandManager commandManager, MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(commandManager);
        ArgumentNullException.ThrowIfNull(addon);
        Plugin = plugin;
        Framework = framework;
        CommandManager = commandManager;
        Addon = addon;
    }
}
