using System;

namespace Mahjong.Plugin.Dalamud.Actions;

public static class HumanTiming
{
    private static readonly Random rng = new();

    public static TimeSpan RandomDelay(
        double medianMs = 900.0,
        double sigma = 0.45,
        double floorMs = 400.0,
        double capMs = 2500.0)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double stdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        double ms = Math.Exp(Math.Log(medianMs) + sigma * stdNormal);
        ms = Math.Clamp(ms, floorMs, capMs);
        return TimeSpan.FromMilliseconds(ms);
    }
}
