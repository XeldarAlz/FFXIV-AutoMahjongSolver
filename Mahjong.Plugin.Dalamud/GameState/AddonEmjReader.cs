using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using Mahjong.Plugin.Dalamud.Logging;

namespace Mahjong.Plugin.Dalamud.GameState;

/// <summary>Must be created on and disposed from the framework thread.</summary>
public sealed class AddonEmjReader : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;
    private readonly MahjongAddon addon;
    private readonly MeldTracker meldTracker;
    private readonly VariantSelector selector;
    private readonly IFindingsLog? findings;
    private bool disposed;
    private bool emittedFirstLifecycle;
    private bool emittedDimensionsZero;
    private bool emittedAtkValuesAnomaly;
    private bool? lastPollPresent;
    private bool inSnapshotFailureStreak;

    /// <summary>Set by Plugin.cs after construction to break the AddonReader/EventLogger ctor cycle; TryBuildSnapshot guards on null.</summary>
    public InputEventLogger? EventLogger { get; set; }

    internal VariantSelector Selector => selector;

    public AddonEmjObservation LastObservation { get; private set; } = AddonEmjObservation.Empty;

    public LayoutProfile? ActiveLayout { get; private set; }

    public event Action<AddonEmjObservation>? ObservationChanged;

    public AddonEmjReader(
        IAddonLifecycle addonLifecycle,
        IPluginLog log,
        MahjongAddon addon,
        MeldTracker meldTracker,
        string pluginConfigDir,
        string layoutsDir,
        IFindingsLog? findings = null)
    {
        ArgumentNullException.ThrowIfNull(addonLifecycle);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentNullException.ThrowIfNull(meldTracker);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDir);
        ArgumentException.ThrowIfNullOrEmpty(layoutsDir);
        this.addonLifecycle = addonLifecycle;
        this.log = log;
        this.addon = addon;
        this.meldTracker = meldTracker;
        this.findings = findings;

        selector = new VariantSelector(LoadRegisteredVariants(log, pluginConfigDir, layoutsDir, findings), log, findings);

        // Some clients expose "Emj", others "EmjL" — register against every candidate name.
        var names = MahjongAddon.CandidateNames;
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, names, OnPostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, names, OnPreFinalize);
        addonLifecycle.RegisterListener(AddonEvent.PostRefresh, names, OnPostRefresh);
        addonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, names, OnPostReceiveEvent);
    }

    private static IReadOnlyList<IEmjVariant> LoadRegisteredVariants(
        IPluginLog log, string pluginConfigDir, string layoutsDir, IFindingsLog? findings)
    {
        try
        {
            var profiles = JsonLayoutProfileLoader.LoadAll(layoutsDir);
            var variants = new List<IEmjVariant>(profiles.Count);
            foreach (var p in profiles)
                variants.Add(new BaseEmjVariant(p, log, pluginConfigDir));
            log.Info(
                $"[MjAuto] Loaded {variants.Count} layout profile(s) from {layoutsDir}: " +
                $"{string.Join(", ", variants.ConvertAll(v => v.Name))}");
            findings?.Record("layouts_loaded", new Dictionary<string, object?>
            {
                ["dir"] = PathRedactor.Redact(layoutsDir),
                ["count"] = variants.Count,
                ["names"] = variants.Select(v => v.Name).ToArray(),
            });
            return variants;
        }
        catch (Exception ex)
        {
            log.Error($"[MjAuto] Layout profile load failed at {layoutsDir}: {ex.Message}");
            findings?.Record("layouts_load_fail", new Dictionary<string, object?>
            {
                ["dir"] = PathRedactor.Redact(layoutsDir),
                ["exception_type"] = ex.GetType().FullName,
                ["message"] = ex.Message,
            });
            return [];
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        addonLifecycle.UnregisterListener(OnPostSetup);
        addonLifecycle.UnregisterListener(OnPreFinalize);
        addonLifecycle.UnregisterListener(OnPostRefresh);
        addonLifecycle.UnregisterListener(OnPostReceiveEvent);
    }

    private void OnPostSetup(AddonEvent type, AddonArgs args) => Observe("PostSetup", args);
    private void OnPostRefresh(AddonEvent type, AddonArgs args) => Observe("PostRefresh", args);
    private void OnPostReceiveEvent(AddonEvent type, AddonArgs args) => Observe("PostReceiveEvent", args);

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        if (LastObservation.Present)
        {
            findings?.Record("addon_unload", new Dictionary<string, object?>
            {
                ["addon_name"] = args.AddonName,
                ["was_visible"] = LastObservation.IsVisible,
                ["last_address"] = LastObservation.Address.ToInt64(),
                ["last_event"] = LastObservation.LastLifecycleEvent,
            });
        }
        emittedFirstLifecycle = false;

        LastObservation = AddonEmjObservation.Empty with { LastLifecycleEvent = "PreFinalize" };
        ObservationChanged?.Invoke(LastObservation);
    }

    private unsafe void Observe(string eventName, AddonArgs args)
    {
        var addr = args.Addon.Address;
        var obs = AddonEmjObservation.Empty;

        if (addr != 0)
        {
            var unit = (AtkUnitBase*)addr;
            obs = new AddonEmjObservation(
                Present: true,
                IsVisible: unit->IsVisible,
                Address: addr,
                Width: unit->RootNode != null ? unit->RootNode->Width : (ushort)0,
                Height: unit->RootNode != null ? unit->RootNode->Height : (ushort)0,
                LastSeenUtcTicks: DateTime.UtcNow.Ticks,
                LastLifecycleEvent: eventName);

            EmitFirstAttachFindings(eventName, args.AddonName, unit, addr, obs);
        }

        LastObservation = obs;
        ObservationChanged?.Invoke(obs);
    }

    /// <summary>Called from both Observe and Poll — Poll catches plugin loads where the addon is already open and no PostSetup event will fire.</summary>
    private unsafe void EmitFirstAttachFindings(
        string eventName, string addonName, AtkUnitBase* unit, nint addr, AddonEmjObservation obs)
    {
        if (!emittedFirstLifecycle)
        {
            emittedFirstLifecycle = true;
            findings?.Record("addon_lifecycle", new Dictionary<string, object?>
            {
                ["event"] = eventName,
                ["addon_name"] = addonName,
                ["address"] = addr.ToInt64(),
                ["width"] = obs.Width,
                ["height"] = obs.Height,
                ["is_visible"] = unit->IsVisible,
                ["atk_values_count"] = (int)unit->AtkValuesCount,
            });
        }

        if (unit->RootNode == null && !emittedDimensionsZero)
        {
            emittedDimensionsZero = true;
            findings?.Record("addon_dimensions_zero", new Dictionary<string, object?>
            {
                ["addon_name"] = addonName,
                ["address"] = addr.ToInt64(),
                ["event"] = eventName,
                ["is_visible"] = unit->IsVisible,
            });
        }

        int atkCount = (int)unit->AtkValuesCount;
        if ((atkCount == 0 || atkCount > 1024) && !emittedAtkValuesAnomaly)
        {
            emittedAtkValuesAnomaly = true;
            findings?.Record("atk_values_anomaly", new Dictionary<string, object?>
            {
                ["addon_name"] = addonName,
                ["address"] = addr.ToInt64(),
                ["atk_values_count"] = atkCount,
                ["kind"] = atkCount == 0 ? "empty" : "oversize",
            });
        }
    }

    /// <summary>Fallback for plugin loads after the addon is already open — Dalamud only fires lifecycle events that happen after listener registration. Framework-thread only.</summary>
    public unsafe AddonEmjObservation Poll()
    {
        if (!addon.TryGet(out var unit, out var resolvedName))
        {
            var missing = AddonEmjObservation.Empty with
            {
                LastSeenUtcTicks = DateTime.UtcNow.Ticks,
                LastLifecycleEvent = LastObservation.LastLifecycleEvent,
            };
            EmitPollPresentChange(false, addonName: null, address: 0);
            LastObservation = missing;
            return missing;
        }

        nint addr = (nint)unit;
        var obs = new AddonEmjObservation(
            Present: true,
            IsVisible: unit->IsVisible,
            Address: addr,
            Width: unit->RootNode != null ? unit->RootNode->Width : (ushort)0,
            Height: unit->RootNode != null ? unit->RootNode->Height : (ushort)0,
            LastSeenUtcTicks: DateTime.UtcNow.Ticks,
            LastLifecycleEvent: LastObservation.LastLifecycleEvent ?? "(poll)");

        EmitFirstAttachFindings("poll", resolvedName, unit, addr, obs);

        EmitPollPresentChange(true, resolvedName, addr);
        LastObservation = obs;
        return obs;
    }

    private void EmitPollPresentChange(bool present, string? addonName, nint address)
    {
        if (lastPollPresent == present)
            return;
        var prev = lastPollPresent;
        lastPollPresent = present;
        if (prev is null)
            return;
        findings?.Record("poll_present_change", new Dictionary<string, object?>
        {
            ["from"] = prev.Value,
            ["to"] = present,
            ["addon_name"] = addonName,
            ["address"] = address.ToInt64(),
            ["last_lifecycle_event"] = LastObservation.LastLifecycleEvent,
        });
    }

    /// <summary>Raw addon ints at HandArrayStart+i*4 — bypasses zero-termination that hides tiles parked past a gap.</summary>
    public unsafe int[]? DumpHandArrayRaw()
    {
        if (ActiveLayout is null)
            return null;
        if (!addon.TryGet(out var unit, out _))
            return null;
        if (!unit->IsVisible)
            return null;

        int len = ActiveLayout.Limits.HandSize;
        var slots = new int[len];
        byte* basePtr = (byte*)unit;
        for (int i = 0; i < len; i++)
            slots[i] = *(int*)(basePtr + ActiveLayout.Offsets.HandArrayStart + i * 4);
        return slots;
    }

    /// <summary>Addon-slot of the first hand-array entry decoding to <paramref name="target"/>; prefers slot 13. Returns -1 if not found.</summary>
    public unsafe int FindAddonSlotOfTile(Mahjong.Core.Tile target)
    {
        if (ActiveLayout is null)
            return -1;
        if (!addon.TryGet(out var unit, out _))
            return -1;
        if (!unit->IsVisible)
            return -1;

        int len = ActiveLayout.Limits.HandSize;
        Span<int> raw = stackalloc int[len];
        byte* basePtr = (byte*)unit;
        for (int i = 0; i < len; i++)
            raw[i] = *(int*)(basePtr + ActiveLayout.Offsets.HandArrayStart + i * 4);
        return Variants.HandArrayDecoder.FindAddonSlot(raw, ActiveLayout.TileTextureBase, target.Id);
    }

    public unsafe StateSnapshot? TryBuildSnapshot()
    {
        if (!addon.TryGet(out var unit, out var resolvedName))
            return null;
        if (!unit->IsVisible)
            return null;

        var variant = selector.Resolve(unit, resolvedName);
        if (variant is null)
            return null;

        ActiveLayout = variant.Profile;

        if (EventLogger is null)
            return null;

        var snap = variant.TryBuildSnapshot(
            unit,
            new VariantReadContext(meldTracker, EventLogger));

        // Null after a successful probe means right shape, wrong offsets — emit one finding per failure streak.
        if (snap is null && !inSnapshotFailureStreak)
        {
            inSnapshotFailureStreak = true;
            findings?.Record("snapshot_build_fail", new Dictionary<string, object?>
            {
                ["addon_name"] = resolvedName,
                ["variant"] = variant.Name,
                ["addon_address"] = ((nint)unit).ToInt64(),
                ["atk_values_count"] = (int)unit->AtkValuesCount,
            });
        }
        else if (snap is not null && inSnapshotFailureStreak)
        {
            inSnapshotFailureStreak = false;
            findings?.Record("snapshot_build_recover", new Dictionary<string, object?>
            {
                ["addon_name"] = resolvedName,
                ["variant"] = variant.Name,
            });
        }

        return snap;
    }
}
