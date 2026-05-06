using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DomanMahjongAI.Actions;
using DomanMahjongAI.Commands;
using DomanMahjongAI.GameState;
using DomanMahjongAI.Hooks;
using DomanMahjongAI.Logging;
using DomanMahjongAI.Policy;
using DomanMahjongAI.Policy.Efficiency;
using DomanMahjongAI.Policy.Mcts;
using DomanMahjongAI.UI;

namespace DomanMahjongAI;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("DomanMahjongAI");
    public Configuration Configuration { get; }
    public MainWindow MainWindow { get; }
    public AboutWindow AboutWindow { get; }
    public DebugOverlay DebugOverlay { get; }
    public HandOverlay HandOverlay { get; }
    public AddonEmjReader AddonReader { get; }
    public MeldTracker MeldTracker { get; } = new();
    public StateAggregator Aggregator { get; }
    public IPolicy Policy { get; private set; }
    public IPolicy EfficiencyPolicyInstance { get; }
    public IPolicy IsmctsPolicyInstance { get; }
    public InputEventLogger EventLogger { get; }
    public InputDispatcher Dispatcher { get; } = new();
    public GameLogger GameLogger { get; }
    public AutoPlayLoop AutoPlay { get; }
    public DiscardHook DiscardHook { get; }

    private readonly MjAutoCommand command;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        AddonReader = new AddonEmjReader(this);
        Aggregator = new StateAggregator(AddonReader);
        EfficiencyPolicyInstance = new EfficiencyPolicy();
        IsmctsPolicyInstance = new IsmctsPolicy();
        Policy = Configuration.PolicyTier == "mcts" ? IsmctsPolicyInstance : EfficiencyPolicyInstance;
        EventLogger = new InputEventLogger(AddonReader, MeldTracker);
        GameLogger = new GameLogger(Aggregator, Configuration);
        AutoPlay = new AutoPlayLoop(this);
        // Mid-function asm hook on the discard handler at ffxiv_dx11.exe+0x1A20A43.
        // Captures every (pool_base, tile_id) tuple as the game commits each
        // discard. Verified empirically via Cheat Engine 2026-04-27. If sigscan
        // fails (e.g. patched binary), the hook stays inert and the rest of the
        // plugin still works.
        DiscardHook = new DiscardHook();

        MainWindow = new MainWindow(this);
        AboutWindow = new AboutWindow();
        DebugOverlay = new DebugOverlay(this);
        HandOverlay = new HandOverlay(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(AboutWindow);
        WindowSystem.AddWindow(DebugOverlay);

        command = new MjAutoCommand(this);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;

        Log.Information("Doman Mahjong Solver loaded.");
    }

    public void Dispose()
    {
        command.Dispose();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        AboutWindow.Dispose();
        DebugOverlay.Dispose();
        HandOverlay.Dispose();
        AutoPlay.Dispose();
        DiscardHook.Dispose();
        GameLogger.Dispose();
        EventLogger.Dispose();
        Aggregator.Dispose();
        AddonReader.Dispose();
    }

    public void ToggleMainWindow() => MainWindow.Toggle();

    public void ToggleAboutWindow() => AboutWindow.Toggle();

    public void ToggleDebugOverlay() => DebugOverlay.Toggle();

    public void SetPolicy(string tier)
    {
        var t = tier.ToLowerInvariant();
        Policy = t == "mcts" ? IsmctsPolicyInstance : EfficiencyPolicyInstance;
        Configuration.PolicyTier = t == "mcts" ? "mcts" : "efficiency";
        Configuration.Save();
    }
}
