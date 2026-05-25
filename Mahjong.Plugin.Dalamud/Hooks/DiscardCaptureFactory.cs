using System;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.Hooks.Strategies;
using Mahjong.Plugin.Dalamud.Telemetry;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Hooks;

/// <summary>Always returns AddonPollDiscardCapture — the native-asm sig collides with idle code on post-2026-05 builds. SigscanProbe still records sig drift to telemetry.</summary>
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
        _ = seatPools;

        SigscanProbe.ProbeDiscardHandler(sigScanner, sigprobes ?? NullSigprobeLog.Instance);

        log.Info(
            "[DiscardCapture] using addon-poll strategy (sigscan recorded for telemetry; " +
            "asm hook disabled until a verified discard-handler sig lands).");
        var fallback = new AddonPollDiscardCapture(log);
        aggregator.Changed += fallback.OnSnapshotChanged;
        return fallback;
    }
}
