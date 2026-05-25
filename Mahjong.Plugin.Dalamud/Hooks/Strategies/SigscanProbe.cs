using System;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.Telemetry;

namespace Mahjong.Plugin.Dalamud.Hooks.Strategies;

internal static class SigscanProbe
{
    public static nint ProbeDiscardHandler(ISigScanner sigScanner, ISigprobeLog sigprobes)
    {
        ArgumentNullException.ThrowIfNull(sigScanner);
        ArgumentNullException.ThrowIfNull(sigprobes);

        const string sigName = "doman.discard-handler";
        const string pattern = NativeAsmDiscardCapture.DiscardSig;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var match = sigScanner.ScanText(pattern);
            sw.Stop();
            sigprobes.Record(
                sigName: sigName,
                pattern: pattern,
                matchAddress: match,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                success: true);
            return match;
        }
        catch (Exception ex)
        {
            sw.Stop();
            sigprobes.Record(
                sigName: sigName,
                pattern: pattern,
                matchAddress: 0,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                success: false,
                errorMessage: ex.Message);
            return 0;
        }
    }
}
