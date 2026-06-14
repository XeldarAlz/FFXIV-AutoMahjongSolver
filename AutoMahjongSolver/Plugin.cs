using AutoMahjongSolver.Core;
using AutoMahjongSolver.Core.Game;
using AutoMahjongSolver.Windows;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AutoMahjongSolver;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;
    internal static Configuration Cfg { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal WindowSystem WindowSystem { get; } = new("AutoMahjongSolver");

    internal AutoPlayController AutoPlay { get; }

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly AboutWindow aboutWindow;
    private readonly EmjCaptureLogger captureLogger;
    private readonly SuggestionOverlay suggestionOverlay;

    public Plugin()
    {
        Instance = this;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Cfg = Configuration;

        AutoPlay = new AutoPlayController();
        captureLogger = new EmjCaptureLogger();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        aboutWindow = new AboutWindow();
        suggestionOverlay = new SuggestionOverlay(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(aboutWindow);

        CommandManager.AddHandler(AmjsConstants.PrimaryCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the window. /amjs config | about | auto | suggest. Debug: dump|tree|board|melds|capture.",
        });
        CommandManager.AddHandler(AmjsConstants.AliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /amjs.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw += suggestionOverlay.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= suggestionOverlay.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
        aboutWindow.Dispose();

        AutoPlay.Dispose();
        captureLogger.Dispose();

        CommandManager.RemoveHandler(AmjsConstants.PrimaryCommand);
        CommandManager.RemoveHandler(AmjsConstants.AliasCommand);
    }

    private void OnCommand(string command, string args)
    {
        string[] parts = args.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length == 0 ? string.Empty : parts[0])
        {
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            case "about":
                ToggleAboutUi();
                break;
            case "dump":
                EmjAddonDumper.Dump();
                break;
            case "tree":
                EmjAddonDumper.DumpTree();
                break;
            case "board":
                EmjAddonDumper.DumpBoard();
                break;
            case "state":
                EmjAddonDumper.DumpState();
                break;
            case "melds":
                EmjAddonDumper.DumpMelds();
                break;
            case "capture":
                string sub = parts.Length > 1 ? parts[1] : "toggle";
                if (sub == "on")
                    captureLogger.SetCapturing(true);
                else if (sub == "off")
                    captureLogger.SetCapturing(false);
                else
                    captureLogger.Toggle();
                break;
            case "discard":
                if (parts.Length > 1 && int.TryParse(parts[1], out int slot))
                    EmjActuator.DiscardSlot(slot);
                else
                    AmjsLog.Info("Usage: /amjs discard <slot 0-13>");
                break;
            case "auto":
            case "suggest":
                EngageCommand(parts[0] == "suggest" ? PlayMode.Suggest : PlayMode.Auto,
                    parts.Length > 1 ? parts[1] : "toggle");
                break;
            case "values":
                EmjAddonDumper.DumpValues();
                break;
            case "options":
                AutoPlayController.LogOptions();
                break;
            case "advance":
                EmjActuator.Advance();
                break;
            default:
                ToggleMainUi();
                break;
        }
    }

    private void EngageCommand(PlayMode mode, string verb)
    {
        switch (verb)
        {
            case "off":
                AutoPlay.SetRunning(false);
                break;
            case "on":
                AutoPlay.SetMode(mode);
                AutoPlay.SetRunning(true);
                break;
            default: // toggle: off if already engaged in this mode, otherwise engage in it
                if (AutoPlay.Running && AutoPlay.Mode == mode)
                {
                    AutoPlay.SetRunning(false);
                }
                else
                {
                    AutoPlay.SetMode(mode);
                    AutoPlay.SetRunning(true);
                }
                break;
        }
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void ToggleAboutUi() => aboutWindow.Toggle();
}
