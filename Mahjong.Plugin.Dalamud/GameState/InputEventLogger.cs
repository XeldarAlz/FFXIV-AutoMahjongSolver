using System;
using System.Globalization;
using System.IO;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;

namespace Mahjong.Plugin.Dalamud.GameState;

public sealed class InputEventLogger : IDisposable
{
    // Sig covers the callsite; HookFromSignature follows the E8 to the real FireCallback.
    private const string FireCallbackSig = "E8 ?? ?? ?? ?? 0F B6 E8 8B 44 24 20";
    private unsafe delegate bool FireCallbackDelegate(AtkUnitBase* addon, uint valueCount, AtkValue* values, byte close);

    private const double CaptureTimeoutSeconds = 60.0;

    private readonly AddonEmjReader reader;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameInteropProvider gameInterop;
    private readonly IPluginLog log;
    private readonly MahjongAddon addon;
    private readonly string logPath;
    private readonly string capturePath;
    private StreamWriter? writer;
    private bool disposed;
    private unsafe Hook<FireCallbackDelegate>? fireCallbackHook;

    private string? pendingCaptureLabel;
    private DateTime captureArmedAt;

    /// <summary>Getter expires stale labels lazily.</summary>
    public string? PendingCaptureLabel
    {
        get
        {
            if (pendingCaptureLabel is not null
                && (DateTime.UtcNow - captureArmedAt).TotalSeconds > CaptureTimeoutSeconds)
            {
                pendingCaptureLabel = null;
            }
            return pendingCaptureLabel;
        }
    }

    public bool Enabled { get; set; }

    public string CaptureLogPath => capturePath;

    /// <summary>Fires after the original FireCallback; always-on, Enabled only gates the verbose RE log.</summary>
    public event Action<InputCallbackEvent>? CallbackObserved;

    /// <summary>Fires before the original FireCallback so subscribers see pre-click addon state.</summary>
    public event Action<string>? BeforeFireCallback;

    /// <summary>Fires on every call-prompt transition observed by a variant.</summary>
    public event Action<CallPromptEvent>? CallPromptObserved;

    internal void RaiseCallPrompt(CallPromptEvent evt)
    {
        if (CallPromptObserved is not { } observers)
            return;
        try
        { observers(evt); }
        catch (Exception ex)
        { log.Error($"CallPromptObserved subscriber error: {ex.Message}"); }
    }

    public unsafe InputEventLogger(
        AddonEmjReader reader,
        IAddonLifecycle addonLifecycle,
        IGameInteropProvider gameInterop,
        IPluginLog log,
        MahjongAddon addon,
        string pluginConfigDir)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(addonLifecycle);
        ArgumentNullException.ThrowIfNull(gameInterop);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDir);
        this.reader = reader;
        this.addonLifecycle = addonLifecycle;
        this.gameInterop = gameInterop;
        this.log = log;
        this.addon = addon;

        Directory.CreateDirectory(pluginConfigDir);
        logPath = Path.Combine(pluginConfigDir, "emj-events.log");
        capturePath = Path.Combine(pluginConfigDir, "emj-captures.log");

        addonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, MahjongAddon.CandidateNames, OnReceiveEvent);

        try
        {
            fireCallbackHook = gameInterop.HookFromSignature<FireCallbackDelegate>(
                FireCallbackSig, FireCallbackDetour);
            fireCallbackHook.Enable();
        }
        catch (Exception ex)
        {
            log.Error($"InputEventLogger: failed to hook FireCallback: {ex}");
            fireCallbackHook = null;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        addonLifecycle.UnregisterListener(OnReceiveEvent);
        fireCallbackHook?.Disable();
        fireCallbackHook?.Dispose();
        fireCallbackHook = null;
        writer?.Flush();
        writer?.Dispose();
    }

    public string LogPath => logPath;

    public void ArmCapture(string label)
    {
        pendingCaptureLabel = label;
        captureArmedAt = DateTime.UtcNow;
    }

    public void DisarmCapture()
    {
        pendingCaptureLabel = null;
    }

    public void OpenLog()
    {
        writer ??= new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public void CloseLog()
    {
        writer?.Flush();
        writer?.Dispose();
        writer = null;
    }

    private unsafe bool FireCallbackDetour(AtkUnitBase* addon, uint valueCount, AtkValue* values, byte close)
    {
        // Capture snapshot must run BEFORE the original FireCallback — the original may mutate the addon (close modal, refresh AtkValues).
        string? captureLabel = null;
        string? captureHand = null;
        string[]? captureFireArgs = null;
        string[]? captureAtkValues = null;
        int captureAtkCount = 0;
        if (PendingCaptureLabel is { } pending
            && addon != null && MahjongAddon.IsMahjongAddon(addon->NameString))
        {
            captureLabel = pending;
            captureFireArgs = SnapshotValues(values, (int)valueCount, max: 32);
            captureAtkCount = addon->AtkValuesCount;
            captureAtkValues = SnapshotValues(addon->AtkValues, captureAtkCount, max: 64);
            var preSnap = reader.TryBuildSnapshot();
            if (preSnap is not null && preSnap.Hand.Count > 0)
                captureHand = Tiles.Render(preSnap.Hand);
        }

        // Subscribers read addon state synchronously here so it still reflects pre-click memory.
        if (BeforeFireCallback is { } preObservers
            && addon != null && MahjongAddon.IsMahjongAddon(addon->NameString))
        {
            try
            { preObservers(addon->NameString); }
            catch (Exception ex)
            { log.Error($"BeforeFireCallback subscriber error: {ex.Message}"); }
        }

        // Call original FIRST so game logic is unaffected regardless of logger state.
        bool result = fireCallbackHook!.Original(addon, valueCount, values, close);

        if (CallbackObserved is { } observers
            && addon != null && MahjongAddon.IsMahjongAddon(addon->NameString))
        {
            try
            {
                var ints = SnapshotInts(values, (int)valueCount, max: 24);
                observers(new InputCallbackEvent(
                    ObservedAtUtc: DateTime.UtcNow,
                    AddonName: addon->NameString,
                    ValueCount: valueCount,
                    Close: close != 0,
                    Result: result,
                    IntValues: ints));
            }
            catch (Exception ex)
            {
                log.Error($"FireCallback observer error: {ex.Message}");
            }
        }

        try
        {
            if (Enabled && addon != null && MahjongAddon.IsMahjongAddon(addon->NameString))
            {
                OpenLog();
                var sb = new StringBuilder();
                sb.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                sb.Append($"  evt=FireCallback  count={valueCount}  close={(close != 0)}  result={result}");

                var snap = reader.TryBuildSnapshot();
                if (snap is not null && snap.Hand.Count > 0)
                {
                    sb.Append("  hand=");
                    sb.Append(Tiles.Render(snap.Hand));
                }

                if (values != null && valueCount > 0)
                {
                    sb.Append("  values=[");
                    uint cap = valueCount > 16 ? 16 : valueCount;
                    for (uint i = 0; i < cap; i++)
                    {
                        var v = values[i];
                        sb.Append($"{i}:{v.Type}=");
                        switch (v.Type)
                        {
                            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int:
                                sb.Append(v.Int);
                                break;
                            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt:
                                sb.Append(v.UInt);
                                break;
                            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool:
                                sb.Append(v.Byte != 0);
                                break;
                            default:
                                sb.Append($"raw=0x{v.UInt:X}");
                                break;
                        }
                        if (i < cap - 1)
                            sb.Append(',');
                    }
                    if (valueCount > cap)
                        sb.Append($"...+{valueCount - cap}");
                    sb.Append(']');
                }

                writer?.WriteLine(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            log.Error($"FireCallback log error: {ex.Message}");
        }

        if (captureLabel is not null)
        {
            try
            {
                WriteCaptureEntry(
                    captureLabel, captureHand, captureFireArgs!, valueCount,
                    captureAtkValues!, captureAtkCount, close, result);
            }
            catch (Exception ex)
            {
                log.Error($"FireCallback capture error: {ex.Message}");
            }
            finally
            {
                pendingCaptureLabel = null;
            }
        }

        return result;
    }

    private unsafe void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!Enabled)
            return;
        OpenLog();

        var addr = args.Addon.Address;
        if (addr == 0)
            return;

        var sb = new StringBuilder();
        sb.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        sb.Append("  evt=PostReceiveEvent");

        if (args is AddonReceiveEventArgs rea)
        {
            sb.Append($"  type={rea.AtkEventType}  param={rea.EventParam}");
        }

        var snap = reader.TryBuildSnapshot();
        if (snap is not null && snap.Hand.Count > 0)
        {
            sb.Append("  hand=");
            sb.Append(Tiles.Render(snap.Hand));
        }

        var unit = (AtkUnitBase*)addr;
        int valueCount = Math.Min((int)unit->AtkValuesCount, 8);
        if (valueCount > 0)
        {
            sb.Append("  atk=[");
            for (int i = 0; i < valueCount; i++)
            {
                var v = unit->AtkValues[i];
                sb.Append($"{i}:{v.Type}=");
                switch (v.Type)
                {
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int:
                        sb.Append(v.Int);
                        break;
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt:
                        sb.Append(v.UInt);
                        break;
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool:
                        sb.Append(v.Byte != 0);
                        break;
                    default:
                        sb.Append("?");
                        break;
                }
                if (i < valueCount - 1)
                    sb.Append(',');
            }
            sb.Append(']');
        }

        writer?.WriteLine(sb.ToString());
    }

    private void WriteCaptureEntry(
        string label, string? hand, string[] fireArgs, uint fireArgCount,
        string[] atkValues, int atkCount, byte close, bool result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"# {DateTime.UtcNow:o}  label={label}  result={result}  close={(close != 0)}  " +
            $"valueCount={fireArgCount}");

        if (hand is not null)
            sb.AppendLine($"hand={hand}");

        sb.AppendLine($"fire_args (count={fireArgCount}):");
        for (int i = 0; i < fireArgs.Length; i++)
            sb.AppendLine($"  [{i,3}] {fireArgs[i]}");
        if (fireArgCount > fireArgs.Length)
            sb.AppendLine($"  ... +{fireArgCount - fireArgs.Length} more");

        sb.AppendLine($"addon_atkvalues (count={atkCount}):");
        for (int i = 0; i < atkValues.Length; i++)
            sb.AppendLine($"  [{i,3}] {atkValues[i]}");
        if (atkCount > atkValues.Length)
            sb.AppendLine($"  ... +{atkCount - atkValues.Length} more");

        sb.AppendLine();
        File.AppendAllText(capturePath, sb.ToString());
        log.Info(
            $"[capture] recorded label={label} (result={result}) → {capturePath}");
    }

    private static unsafe string[] SnapshotValues(AtkValue* values, int count, int max)
    {
        if (values == null || count <= 0)
            return Array.Empty<string>();
        int n = Math.Min(count, max);
        var result = new string[n];
        for (int i = 0; i < n; i++)
            result[i] = FormatValue(values[i]);
        return result;
    }

    private static unsafe int?[] SnapshotInts(AtkValue* values, int count, int max)
    {
        if (values == null || count <= 0)
            return Array.Empty<int?>();
        int n = Math.Min(count, max);
        var result = new int?[n];
        for (int i = 0; i < n; i++)
        {
            var v = values[i];
            result[i] = v.Type switch
            {
                FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int => v.Int,
                FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt => unchecked((int)v.UInt),
                FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool => v.Byte != 0 ? 1 : 0,
                _ => (int?)null,
            };
        }
        return result;
    }

    private static unsafe string FormatValue(AtkValue v)
    {
        switch (v.Type)
        {
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int:
                return $"{v.Type,-14} Int={v.Int}";
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt:
                return $"{v.Type,-14} UInt={v.UInt} (0x{v.UInt:X})";
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool:
                return $"{v.Type,-14} Bool={v.Byte != 0}";
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String:
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8:
            case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ManagedString:
                var s = v.String.Value != null
                    ? v.String.ToString()
                    : "(null)";
                return $"{v.Type,-14} String=\"{s}\"";
            default:
                return $"{v.Type,-14} raw=0x{v.UInt:X}";
        }
    }
}

public sealed record InputCallbackEvent(
    DateTime ObservedAtUtc,
    string AddonName,
    uint ValueCount,
    bool Close,
    bool Result,
    int?[] IntValues);

public sealed record CallPromptEvent(
    DateTime ObservedAtUtc,
    string AddonName,
    int StateCode,
    int Flags,
    int[] PonClaimedTileIds,
    int[] ChiClaimedTileIds,
    int[] KanClaimedTileIds,
    int?[] IntValues);
