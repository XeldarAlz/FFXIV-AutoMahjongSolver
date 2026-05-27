using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Plugin.Dalamud.Logging;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

internal sealed class VariantSelector
{
    // Suppress the unmatched warning during the post-setup transient where probes legitimately miss.
    private static readonly TimeSpan UnmatchedWarnDelay = TimeSpan.FromSeconds(2);

    private readonly IReadOnlyList<IEmjVariant> variants;
    private readonly IPluginLog log;
    private readonly IFindingsLog? findings;
    private IEmjVariant? cached;
    private bool loggedUnmatched;
    private DateTime? firstMissAt;

    public VariantSelector(IReadOnlyList<IEmjVariant> variants, IPluginLog log, IFindingsLog? findings = null)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(log);
        this.variants = variants;
        this.log = log;
        this.findings = findings;
    }

    public IReadOnlyList<IEmjVariant> Variants => variants;

    public unsafe IEmjVariant? Resolve(AtkUnitBase* unit, string resolvedAddonName)
    {
        // Discard cached match if either the probe or the name tiebreaker no longer agrees.
        if (cached is not null
            && cached.Probe(unit)
            && cached.PreferredAddonName == resolvedAddonName)
        {
            firstMissAt = null;
            return cached;
        }

        var probeResults = new List<(string Name, string Preferred, bool Matched)>(variants.Count);
        IEmjVariant? winner = null;
        foreach (var v in variants)
        {
            bool matched = v.Probe(unit);
            probeResults.Add((v.Name, v.PreferredAddonName, matched));
            if (!matched)
                continue;
            if (winner is null || v.PreferredAddonName == resolvedAddonName)
                winner = v;
        }

        if (winner is not null)
        {
            if (cached != winner)
            {
                log.Info(
                    $"[MjAuto] Emj variant resolved as \"{winner.Name}\" " +
                    $"(addon=\"{resolvedAddonName}\")");
                EmitVariantChange(cached, winner, resolvedAddonName, probeResults);
                cached = winner;
                loggedUnmatched = false;
            }
            firstMissAt = null;
            return cached;
        }

        firstMissAt ??= DateTime.UtcNow;
        if (!loggedUnmatched && DateTime.UtcNow - firstMissAt.Value >= UnmatchedWarnDelay)
        {
            log.Warning(
                "[MjAuto] No Emj variant matched this addon layout. " +
                $"Registered variants: {string.Join(", ", variants.Select(v => v.Name))}. " +
                "Run `/mjauto variant dump` and open a new issue at " +
                "https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues/new with the output attached.");
            EmitVariantMiss(resolvedAddonName, probeResults);
            loggedUnmatched = true;
        }
        cached = null;
        return null;
    }

    private void EmitVariantChange(
        IEmjVariant? from,
        IEmjVariant to,
        string addonName,
        List<(string Name, string Preferred, bool Matched)> probeResults)
    {
        if (findings is null)
            return;
        findings.Record("variant_match", new Dictionary<string, object?>
        {
            ["addon_name"] = addonName,
            ["from"] = from?.Name,
            ["to"] = to.Name,
            ["preferred_match"] = to.PreferredAddonName == addonName,
            ["probes"] = probeResults
                .Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["preferred"] = p.Preferred,
                    ["matched"] = p.Matched,
                })
                .ToArray(),
        });
    }

    private void EmitVariantMiss(
        string addonName,
        List<(string Name, string Preferred, bool Matched)> probeResults)
    {
        if (findings is null)
            return;
        findings.Record("variant_miss", new Dictionary<string, object?>
        {
            ["addon_name"] = addonName,
            ["registered_count"] = variants.Count,
            ["probes"] = probeResults
                .Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["preferred"] = p.Preferred,
                    ["matched"] = p.Matched,
                })
                .ToArray(),
        });
    }
}
