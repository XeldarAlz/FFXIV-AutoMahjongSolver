using System;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.Hooks.Strategies;
using Mahjong.Plugin.Dalamud.Telemetry;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Hooks;

/// <summary>
/// Builds the live <see cref="IDiscardCapture"/>. Currently always returns
/// <see cref="AddonPollDiscardCapture"/>; the native asm hook is constructed
/// briefly so its sigscan still feeds the <c>sigprobes</c> telemetry stream,
/// then disposed without driving discards.
///
/// <para><b>Why not native:</b> on post-2026-05 FFXIV builds the original
/// 20-byte discard-handler signature (verified via Cheat Engine on 2026-04-27)
/// matches an unrelated routine that runs in idle game code with EAX = 0.
/// Real-world telemetry from one play session showed 135 captured "discards"
/// — every one with <c>tile_id = 0</c> (1m), and the bursts started ~5 minutes
/// before the Mahjong addon was even live. AddonPoll has more information
/// anyway (it knows the seat the discard came from) and doesn't depend on
/// any byte pattern; the only trade-off is ~one snapshot tick (~16ms) of
/// latency, which the policy pipeline already absorbs.</para>
///
/// <para><b>Re-enabling native:</b> once a new sig is verified against the
/// current FFXIV build, swap the early <c>Dispose()</c> below for the
/// previous <c>if (native.Health == HookHealth.Active) return native;</c>
/// short-circuit — or gate it on a config flag.</para>
/// </summary>
public static class DiscardCaptureFactory
{
    public static IDiscardCapture Create(
        IPluginLog log,
        IFramework framework,
        ISigScanner sigScanner,
        StateAggregator aggregator,
        SeatPoolRegistry? seatPools = null,
        ISigprobeLog? sigprobes = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(sigScanner);
        ArgumentNullException.ThrowIfNull(aggregator);

        // Run the sigscan + hook activation briefly so the result still flows
        // into the sigprobes stream — the data is useful for tracking when
        // (and which) FFXIV patches break the pattern.
        var native = new NativeAsmDiscardCapture(log, framework, sigScanner, seatPools, sigprobes);
        native.Dispose();

        log.Info(
            "[DiscardCapture] using addon-poll strategy (sigscan recorded for telemetry; " +
            "asm hook disabled until a verified discard-handler sig lands).");
        var fallback = new AddonPollDiscardCapture(log);
        aggregator.Changed += fallback.OnSnapshotChanged;
        return fallback;
    }
}
