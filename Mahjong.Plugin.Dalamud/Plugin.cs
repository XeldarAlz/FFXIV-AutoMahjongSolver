using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Plugin.Dalamud.Adapters;
using Mahjong.Plugin.Dalamud.Commands;
using Mahjong.Plugin.Dalamud.Composition;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.Hooks;
using Mahjong.Plugin.Dalamud.Logging;
using Mahjong.Plugin.Dalamud.Telemetry;
using Mahjong.Plugin.Dalamud.UI;
using Mahjong.Plugin.Game;
using Mahjong.Policy;
using Mahjong.Policy.Efficiency;
using Microsoft.Extensions.DependencyInjection;

namespace Mahjong.Plugin.Dalamud;

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
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("Mahjong.Plugin.Dalamud");

    public ServiceProvider Services { get; }

    /// <summary>Use Update() to mutate — never reach into the underlying record directly.</summary>
    public IConfigService<Configuration> ConfigService { get; }

    /// <summary>Reference is replaced on every edit — read fresh each frame, don't cache.</summary>
    public Configuration Configuration => ConfigService.Current;

    public MainWindow MainWindow { get; }
    public AboutWindow AboutWindow { get; }
    public SettingsWindow SettingsWindow { get; }
    public DebugOverlay DebugOverlay { get; }
    public HandOverlay HandOverlay { get; }
    public AddonEmjReader AddonReader { get; }
    public MeldTracker MeldTracker { get; } = new();
    public StateAggregator Aggregator { get; }
    public IPolicy Policy { get; }
    public InputEventLogger EventLogger { get; }
    public InputDispatcher Dispatcher { get; }
    public GameLogger GameLogger { get; }
    public AutoPlayLoop AutoPlay { get; }

    public IDiscardCapture DiscardCapture { get; }

    public DiscardCaptureLogger DiscardCaptureLogger { get; }

    public ErrorSink ErrorSink { get; }
    public IFindingsLog FindingsLog { get; }
    public ISigprobeLog SigprobeLog { get; }
    public SeatPoolRegistry SeatPoolRegistry { get; } = new();
    public MemoryDumpRecorder MemoryDumpRecorder { get; }
    public TelemetryUploader TelemetryUploader { get; }
    public DiscardTracker DiscardTracker { get; }
    public InputRecorder InputRecorder { get; }

    private readonly System.Net.Http.HttpClient telemetryHttp;
    private readonly MirroredPluginLog mirroredLog = null!;

    public MjAutoCommand MjAutoCommand => command;

    private readonly MjAutoCommand command;

    public Plugin()
    {
        var loaded = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration { Version = 0 };
        var migrators = new IConfigMigrator<Configuration>[]
        {
            new ConfigMigratorV0ToV1(),
            new ConfigMigratorV1ToV2(),
        };
        var migrated = ConfigMigrationRunner.Run(
            loaded,
            currentVersion: loaded.Version,
            targetVersion: Mahjong.Plugin.Dalamud.Configuration.CurrentSchemaVersion,
            migrators);

        // Dev tools never persist across launches — opt-in per session.
        migrated = migrated with { DevMode = false };

        // Persist migrated config so the next launch starts at the current schema version.
        if (!ReferenceEquals(loaded, migrated))
            PluginInterface.SavePluginConfig(migrated);

        // MirroredPluginLog forwards to Dalamud and additionally writes Warning+ to the ErrorSink (attached below).
        mirroredLog = new MirroredPluginLog(Log);
        var dalamud = new DalamudServices(
            Log: mirroredLog,
            Framework: Framework,
            PluginInterface: PluginInterface,
            CommandManager: CommandManager,
            ChatGui: ChatGui,
            ClientState: ClientState,
            DataManager: DataManager,
            Condition: Condition,
            GameGui: GameGui,
            AddonLifecycle: AddonLifecycle,
            SigScanner: SigScanner,
            GameInterop: GameInterop);

        Services = PluginServices.Build(dalamud, migrated);
        ConfigService = Services.GetRequiredService<IConfigService<Configuration>>();

        Policy = Services.GetRequiredService<IPolicy>();

        // Sinks must exist before any reader construction — readers emit findings on probe.
        var configDir = PluginInterface.GetPluginConfigDirectory();
        ErrorSink = new ErrorSink(configDir);
        FindingsLog = new FindingsLog(configDir, ErrorSink);
        SigprobeLog = new SigprobeLog(configDir);
        mirroredLog.AttachSink(ErrorSink);

        // Surface silent meld-tracker drops: the inference loop gives up after MaxDeferralTicks
        // and rebaselines, which is the failure mode behind the post-call out-of-sync stuck
        // state. Without this, the only signal is the downstream "hand state out of sync" pause.
        MeldTracker.DeferralTimedOut += OnMeldTrackerDeferralTimedOut;

        var mahjongAddon = Services.GetRequiredService<MahjongAddon>();

        // IDalamudPluginInterface.AssemblyLocation is the only reliable sibling-file lookup — Assembly.Location is empty inside Dalamud's ALC.
        var pluginAssemblyDir = PluginInterface.AssemblyLocation.DirectoryName ?? configDir;
        var layoutsDir = Path.Combine(pluginAssemblyDir, "layouts");

        AddonReader = new AddonEmjReader(
            AddonLifecycle, Log, mahjongAddon, MeldTracker, configDir, layoutsDir, FindingsLog);
        // Accessor closes over AddonReader so state codes and the hand-array offset follow the active variant. Constructed after AddonReader so the closure resolves to the live profile by the time DispatchDiscard runs.
        Dispatcher = new InputDispatcher(mahjongAddon, () => AddonReader.ActiveLayout);
        Aggregator = new StateAggregator(AddonReader, Framework);
        EventLogger = new InputEventLogger(
            AddonReader, AddonLifecycle, GameInterop, Log, mahjongAddon, configDir);
        AddonReader.EventLogger = EventLogger;
        InputRecorder = new InputRecorder(EventLogger, configDir);
        GameLogger = new GameLogger(
            Aggregator, ConfigService, Log, configDir,
            policyAccessor: () => Policy,
            eventLogger: EventLogger,
            meldTrackerAccessor: () => MeldTracker.SerializeState());
        AutoPlay = new AutoPlayLoop(this, Framework, Log, mahjongAddon);

        DiscardCapture = DiscardCaptureFactory.Create(
            Log, Framework, SigScanner, Aggregator, SeatPoolRegistry, SigprobeLog);
        DiscardCaptureLogger = new DiscardCaptureLogger(
            DiscardCapture, PluginInterface.GetPluginConfigDirectory());
        DiscardTracker = new DiscardTracker(DiscardCapture, configDir);

        var envelope = TelemetryEnvelope.Build(migrated.InstallId, ClientState.ClientLanguage);
        telemetryHttp = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        var http = new HttpTelemetryClient(telemetryHttp, envelope, Log);
        var endpointHolder = new EndpointHolder(
            new TelemetryEndpoint(EndpointResolver.EmbeddedFallbackUrl, true, null));
        TelemetryUploader = new TelemetryUploader(http, endpointHolder, ConfigService, Log, configDir);

        // Async — startup must not block on a GitHub fetch; uploads use the embedded fallback until this completes.
        _ = ResolveEndpointAsync(telemetryHttp, endpointHolder);

        MemoryDumpRecorder = new MemoryDumpRecorder(
            AddonReader, SeatPoolRegistry, ErrorSink, configDir);

        Aggregator.Changed += _ => MemoryDumpRecorder.Record("state-change");

        // Bracket every FireCallback with (pre, post) memdumps. Both labels bypass the atk_count gate that gates "state-change".
        EventLogger.BeforeFireCallback += _ => MemoryDumpRecorder.Record("input-pre");
        EventLogger.CallbackObserved += _ => MemoryDumpRecorder.Record("input-post");

        MainWindow = new MainWindow(this);
        AboutWindow = new AboutWindow(Log, PluginInterface, TextureProvider);
        SettingsWindow = new SettingsWindow(this);
        DebugOverlay = new DebugOverlay(this, Framework, CommandManager, mahjongAddon);
        HandOverlay = new HandOverlay(this, PluginInterface, mahjongAddon);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(AboutWindow);
        WindowSystem.AddWindow(SettingsWindow);
        WindowSystem.AddWindow(DebugOverlay);

        command = new MjAutoCommand(
            this, ChatGui, CommandManager, Framework, PluginInterface, SigScanner, mahjongAddon);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;

        var asmVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "(unknown)";
        Log.Information($"Doman Mahjong Solver v{asmVersion} loaded.");
    }

    private void OnMeldTrackerDeferralTimedOut(DeferralTimeoutEvent evt)
    {
        string removed = string.Join(",", evt.Removed.Select(t => t.ToString()));
        Log.Warning(
            $"[MeldTracker] deferral exhausted: dropping delta={evt.Delta} after {evt.DeferredTicks} ticks. " +
            $"Removed=[{removed}]. Subsequent meld inferences may be off until the next hand.");
        FindingsLog?.Record("meld_tracker_silent_drop", new Dictionary<string, object?>
        {
            ["delta"] = evt.Delta,
            ["deferred_ticks"] = evt.DeferredTicks,
            ["removed_tiles"] = removed,
        });
    }

    public void Dispose()
    {
        MeldTracker.DeferralTimedOut -= OnMeldTrackerDeferralTimedOut;
        command.Dispose();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        AboutWindow.Dispose();
        SettingsWindow.Dispose();
        DebugOverlay.Dispose();
        HandOverlay.Dispose();
        AutoPlay.Dispose();
        DiscardCaptureLogger.Dispose();
        DiscardTracker.Dispose();
        DiscardCapture.Dispose();
        GameLogger.Dispose();
        InputRecorder.Dispose();
        EventLogger.Dispose();
        Aggregator.Dispose();
        AddonReader.Dispose();

        // Flush uploader first (10s hard cap inside) so in-flight POSTs finish before sinks tear down.
        TelemetryUploader.Dispose();
        MemoryDumpRecorder.Dispose();
        (FindingsLog as IDisposable)?.Dispose();
        ErrorSink.Dispose();
        telemetryHttp.Dispose();

        // Services last — container singletons may still be touched by components disposed above.
        Services.Dispose();
    }

    private async System.Threading.Tasks.Task ResolveEndpointAsync(
        System.Net.Http.HttpClient http, EndpointHolder holder)
    {
        try
        {
            var resolved = await EndpointResolver.ResolveAsync(http).ConfigureAwait(false);
            holder.Set(resolved);
            Log.Info($"[Telemetry] endpoint resolved: enabled={resolved.Enabled}");
        }
        catch (Exception ex)
        {
            ErrorSink.RecordException("Plugin.ResolveEndpointAsync", ex);
        }
    }

    public void ToggleMainWindow() => MainWindow.Toggle();

    public void ToggleAboutWindow() => AboutWindow.Toggle();

    public void ToggleSettingsWindow() => SettingsWindow.Toggle();

    public void ToggleDebugOverlay() => DebugOverlay.Toggle();
}
