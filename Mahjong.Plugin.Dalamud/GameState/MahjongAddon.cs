using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mahjong.Plugin.Dalamud.GameState;

public sealed class MahjongAddon
{
    /// <summary>Probed in order — most clients use Emj, some NA/English non-Steam clients use EmjL.</summary>
    public static readonly IReadOnlyList<string> CandidateNames = new[] { "Emj", "EmjL" };

    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private string? lastResolved;

    public MahjongAddon(IGameGui gameGui, IPluginLog log)
    {
        ArgumentNullException.ThrowIfNull(gameGui);
        ArgumentNullException.ThrowIfNull(log);
        this.gameGui = gameGui;
        this.log = log;
    }

    /// <summary>Callers must still check unit-&gt;IsVisible if they care about modal/in-match state.</summary>
    public unsafe bool TryGet(out AtkUnitBase* unit, out string name)
    {
        if (lastResolved is not null)
        {
            var ptr = gameGui.GetAddonByName(lastResolved);
            if (ptr.Address != nint.Zero)
            {
                unit = (AtkUnitBase*)ptr.Address;
                name = lastResolved;
                return true;
            }
        }

        foreach (var candidate in CandidateNames)
        {
            var ptr = gameGui.GetAddonByName(candidate);
            if (ptr.Address == nint.Zero)
                continue;

            if (lastResolved != candidate)
            {
                log.Info(
                    $"[MjAuto] Mahjong addon resolved as \"{candidate}\" " +
                    $"(candidates: {string.Join(", ", CandidateNames)})");
                lastResolved = candidate;
            }
            unit = (AtkUnitBase*)ptr.Address;
            name = candidate;
            return true;
        }

        unit = null;
        name = string.Empty;
        return false;
    }

    public static bool IsMahjongAddon(string addonName)
    {
        foreach (var c in CandidateNames)
            if (addonName == c)
                return true;
        return false;
    }
}
