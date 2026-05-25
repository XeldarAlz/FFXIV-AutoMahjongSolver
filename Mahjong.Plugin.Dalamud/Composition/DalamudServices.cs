using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Mahjong.Plugin.Dalamud.Composition;

public sealed record DalamudServices(
    IPluginLog Log,
    IFramework Framework,
    IDalamudPluginInterface PluginInterface,
    ICommandManager CommandManager,
    IChatGui ChatGui,
    IClientState ClientState,
    IDataManager DataManager,
    ICondition Condition,
    IGameGui GameGui,
    IAddonLifecycle AddonLifecycle,
    ISigScanner SigScanner,
    IGameInteropProvider GameInterop);
