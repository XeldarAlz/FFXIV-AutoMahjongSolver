using System.Text;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoMahjongSolver.Core.Game;

/// <summary>
/// Log-only reverse-engineering aid. While capturing, records the AtkValue arguments the Emj addon
/// passes to FireCallback (the discard/call command protocol) plus the hand state, so auto-play can
/// later replay verified inputs. It NEVER alters arguments and always invokes the original.
/// </summary>
internal sealed unsafe class EmjCaptureLogger : IDisposable
{
    private delegate void FireCallbackDelegate(AtkUnitBase* addon, int valueCount, AtkValue* values, byte updateState);

    private readonly Hook<FireCallbackDelegate>? hook;

    private bool capturing;
    private nint emjPtr;
    private long lastHandTick;
    private string lastHand = string.Empty;

    public bool Capturing => capturing;
    public bool HookReady => hook is not null;

    public EmjCaptureLogger()
    {
        try
        {
            nint address = (nint)AtkUnitBase.Addresses.FireCallback.Value;
            hook = Plugin.GameInteropProvider.HookFromAddress<FireCallbackDelegate>(address, FireCallbackDetour);
            AmjsLog.Debug($"FireCallback hook created @ 0x{address:X}");
        }
        catch (Exception ex)
        {
            AmjsLog.Error($"Could not create FireCallback hook (capture will log hand diffs only): {ex.Message}");
        }
    }

    public void Toggle() => SetCapturing(!capturing);

    public void SetCapturing(bool on)
    {
        if (on == capturing)
            return;
        capturing = on;

        if (on)
        {
            emjPtr = (nint)CurrentAddon();
            lastHand = string.Empty;
            hook?.Enable();
            Plugin.Framework.Update += OnFrameworkUpdate;
            AmjsLog.Info("Capture ON. Play manually on a CASUAL table; discards/calls are logged to /xllog." +
                         (HookReady ? "" : " (callback hook unavailable; hand diffs only)"));
        }
        else
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            hook?.Disable();
            AmjsLog.Info("Capture OFF.");
        }
    }

    private void FireCallbackDetour(AtkUnitBase* addon, int valueCount, AtkValue* values, byte updateState)
    {
        try
        {
            if (capturing && addon != null && (nint)addon == emjPtr)
                LogCallback(addon, valueCount, values, updateState);
        }
        catch (Exception ex)
        {
            AmjsLog.Error($"capture detour: {ex.Message}");
        }

        hook!.Original(addon, valueCount, values, updateState);
    }

    private static void LogCallback(AtkUnitBase* addon, int valueCount, AtkValue* values, byte updateState)
    {
        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;

        var sb = new StringBuilder();
        sb.Append($"FireCallback state={state} updateState={updateState} count={valueCount}:");
        int n = Math.Clamp(valueCount, 0, 16);
        for (int i = 0; i < n; i++)
        {
            AtkValue v = values[i];
            sb.Append($" [{i}]{v.Type}={v.Int}");
        }

        AmjsLog.Info(sb.ToString());

        // For call / chi-variant picks, also dump the popup's button labels so the index map is unambiguous.
        int op = valueCount > 0 ? values[0].Int : -1;
        if (op is 11 or 12)
            EmjAddonDumper.LogValues(addon);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        long now = Environment.TickCount64;
        if (now - lastHandTick < 150)
            return;
        lastHandTick = now;

        emjPtr = (nint)CurrentAddon();
        if (emjPtr == 0)
        {
            lastHand = string.Empty;
            return;
        }

        if (EmjBoardReader.TryReadHand(out HandSnapshot snap) && snap.Notation != lastHand)
        {
            AmjsLog.Info($"hand -> {snap.Notation}{(snap.AkaCount > 0 ? $" (+{snap.AkaCount} aka)" : "")}");
            lastHand = snap.Notation;
        }
    }

    private static AtkUnitBase* CurrentAddon() => EmjBoardReader.TryGetAddon(out AtkUnitBase* a) ? a : null;

    public void Dispose()
    {
        if (capturing)
            Plugin.Framework.Update -= OnFrameworkUpdate;
        hook?.Dispose();
    }
}
