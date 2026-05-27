using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.Commands;

public sealed class MjAutoCommand : IDisposable
{
    private const string Primary = "/mjauto";
    private const string HelpText = "Open the Doman Mahjong Solver window. Type /mjauto help for the full command list.";

    private readonly Plugin plugin;
    private readonly IChatGui chatGui;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ISigScanner sigScanner;
    private readonly MahjongAddon addon;

    private bool autoSnapOn;
    private int autoSnapCounter;
    /// <summary>Live state for the Bug-report tab.</summary>
    internal bool IsAutoSnapOn => autoSnapOn;
    /// <summary>Live state for the Bug-report tab.</summary>
    internal int AutoSnapCount => autoSnapCounter;
    /// <summary>Live state for the Bug-report tab.</summary>
    internal int AutoSnapMaxCountValue => AutoSnapMaxCount;
    private long autoSnapLastMs;
    private ulong autoSnapLastHash;
    private const int AutoSnapMinGapMs = 150;
    private const int AutoSnapMaxCount = 500;

    public MjAutoCommand(
        Plugin plugin,
        IChatGui chatGui,
        ICommandManager commandManager,
        IFramework framework,
        IDalamudPluginInterface pluginInterface,
        ISigScanner sigScanner,
        MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(chatGui);
        ArgumentNullException.ThrowIfNull(commandManager);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(sigScanner);
        ArgumentNullException.ThrowIfNull(addon);
        this.plugin = plugin;
        this.chatGui = chatGui;
        this.commandManager = commandManager;
        this.framework = framework;
        this.pluginInterface = pluginInterface;
        this.sigScanner = sigScanner;
        this.addon = addon;
        commandManager.AddHandler(Primary, new CommandInfo(OnCommand)
        {
            HelpMessage = HelpText,
            ShowInHelp = true,
        });
    }

    public void Dispose()
    {
        if (autoSnapOn)
        {
            plugin.AddonReader.ObservationChanged -= OnAutoSnapObservation;
            autoSnapOn = false;
        }
        commandManager.RemoveHandler(Primary);
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var rest = parts.Length > 1 ? parts[1] : string.Empty;

        switch (sub)
        {
            case "":
            case "open":
                plugin.ToggleMainWindow();
                break;

            case "on":
                plugin.ConfigService.Update(c => c with { AutomationArmed = true });
                chatGui.Print("[MjAuto] Automation armed.");
                break;

            case "off":
                plugin.ConfigService.Update(c => c with { AutomationArmed = false });
                chatGui.Print("[MjAuto] Automation disarmed.");
                break;

            case "debug":
                plugin.ToggleDebugOverlay();
                break;

            case "addons":
                DumpAddons(rest);
                break;

            case "dumpmem":
                DumpMemory(rest);
                break;

            case "atkvalues":
                DumpAtkValues();
                break;

            case "snap":
                HandleSnap(rest);
                break;

            case "autosnap":
                HandleAutoSnap(rest);
                break;

            case "walknodes":
                HandleWalkNodes();
                break;

            case "findtiles":
                HandleFindTiles();
                break;

            case "hexdump":
                HandleHexDump(rest);
                break;

            case "poolslots":
                HandlePoolSlots();
                break;

            case "poolicons":
                HandlePoolIcons();
                break;

            case "discardhook":
            {
                var capture = plugin.DiscardCapture;
                chatGui.Print(
                    $"[MjAuto] DiscardCapture  strategy={capture.StrategyName}  " +
                    $"health={capture.Health}  " +
                    $"totalCaptured={capture.TotalCaptured}  " +
                    $"lastTileId={capture.LastTileId} " +
                    $"→ logging to {plugin.DiscardCaptureLogger.LogPath}");
            }
            break;

            case "log":
                HandleLog(rest);
                break;

            case "capture":
                HandleCapture(rest);
                break;

            case "variant":
                HandleVariant(rest);
                break;

            case "testdiscard":
                HandleTestDiscard(rest);
                break;

            case "pass":
                HandlePass(rest);
                break;

            case "autodiscard":
                HandleAutoDiscard();
                break;

            case "help":
            case "?":
                PrintHelp();
                break;

            default:
                chatGui.PrintError($"[MjAuto] Unknown subcommand: '{sub}'. Type /mjauto help for the command list.");
                break;
        }
    }

    private void PrintHelp()
    {
        chatGui.Print("Doman Mahjong Solver — /mjauto commands");
        chatGui.Print("  /mjauto — open the plugin window");
        chatGui.Print("  /mjauto on | off — arm / disarm automation");
        chatGui.Print("  /mjauto help — show this help");

        if (!plugin.Configuration.DevMode)
        {
            chatGui.Print("  (enable \"Developer tools\" in Settings for debug commands)");
            return;
        }

        chatGui.Print("Developer console:");
        chatGui.Print("  /mjauto debug — toggle the developer console");

        chatGui.Print("Manual override:");
        chatGui.Print("  /mjauto autodiscard — run the policy once and discard");
        chatGui.Print("  /mjauto testdiscard <0..13> — dispatch a discard at a slot");
        chatGui.Print("  /mjauto pass <0..5> — send a raw call-option index");

        chatGui.Print("Capture for bug reports:");
        chatGui.Print("  /mjauto log <on|off> — record clicks to emj-events.log");
        chatGui.Print("  /mjauto capture <label> — capture a single click");
        chatGui.Print("  /mjauto snap <label> — dump addon + agent memory to a file");
        chatGui.Print("  /mjauto autosnap <on|off> — auto-snap on state changes");

        chatGui.Print("Reverse-engineering dumps:");
        chatGui.Print("  /mjauto variant dump — layout dump for new client variants");
        chatGui.Print("  /mjauto walknodes — dump the AtkUld node tree");
        chatGui.Print("  /mjauto findtiles — scan addon/agent memory for tile-encoded values");
        chatGui.Print("  /mjauto poolslots — diff visible discard-pool slots for tile fields");
        chatGui.Print("  /mjauto poolicons — decode discard-pool slot icon ids");
        chatGui.Print("  /mjauto addons [filter] — list loaded addons");
        chatGui.Print("  /mjauto dumpmem [offset] [length] — hex dump of addon memory");
        chatGui.Print("  /mjauto atkvalues — dump the Emj AtkValues array");
        chatGui.Print("  /mjauto hexdump [agent] [start] [end] — hex dump of addon or AgentEmj");
        chatGui.Print("  /mjauto discardhook — print DiscardCapture health");
    }

    internal void HandleAutoDiscard()
    {
        framework.RunOnFrameworkThread(() =>
        {
            var snap = plugin.AddonReader.TryBuildSnapshot();
            if (snap is null)
            {
                chatGui.PrintError("[MjAuto] no snapshot — not in a match.");
                return;
            }

            if (!snap.Legal.Can(ActionFlags.Discard))
            {
                chatGui.PrintError(
                    $"[MjAuto] hand has {snap.Hand.Count} tiles — not a discard state. Wait for your turn.");
                return;
            }

            var choice = plugin.Policy.Choose(snap);
            if (choice.Kind != ActionKind.Discard || choice.DiscardTile is null)
            {
                chatGui.PrintError(
                    $"[MjAuto] policy returned {choice.Kind} — autodiscard only handles Discard. {choice.Reasoning}");
                return;
            }

            var tile = choice.DiscardTile.Value;
            int slot = plugin.AddonReader.FindAddonSlotOfTile(tile);
            if (slot < 0)
            {
                chatGui.PrintError($"[MjAuto] tile {tile} not found in hand — internal error.");
                return;
            }

            var delay = Actions.HumanTiming.RandomDelay();
            chatGui.Print(
                $"[MjAuto] auto-discarding {tile} (slot {slot}) in {delay.TotalMilliseconds:F0}ms. {choice.Reasoning}");

            _ = framework.RunOnTick(() =>
            {
                var result = plugin.Dispatcher.DispatchDiscard(slot);
                chatGui.Print($"[MjAuto] dispatch result: {result}");
            }, delay);
        });
    }

    internal void HandlePass(string arg)
    {
        if (!int.TryParse(arg.Trim(), out int opt) || opt is < 0 or > 5)
        {
            chatGui.PrintError("[MjAuto] Usage: /mjauto pass <0..5>  (0=leftmost, higher=rightward; rightmost = pass)");
            return;
        }
        framework.RunOnFrameworkThread(() =>
        {
            var result = plugin.Dispatcher.DispatchCallOption(opt);
            chatGui.Print($"[MjAuto] pass opt={opt} → {result}");
        });
    }

    internal void HandleTestDiscard(string arg)
    {
        if (!int.TryParse(arg.Trim(), out int slot) || slot is < 0 or > 13)
        {
            chatGui.PrintError("[MjAuto] Usage: /mjauto testdiscard <0..13>");
            return;
        }

        framework.RunOnFrameworkThread(() =>
        {
            var result = plugin.Dispatcher.DispatchDiscard(slot);
            chatGui.Print($"[MjAuto] testdiscard slot={slot} result={result}");
        });
    }

    internal void HandleCapture(string arg)
    {
        var label = arg.Trim();
        if (string.IsNullOrEmpty(label))
        {
            var pending = plugin.EventLogger.PendingCaptureLabel;
            if (pending != null)
            {
                plugin.EventLogger.DisarmCapture();
                chatGui.Print($"[MjAuto] capture disarmed (was: {pending}).");
            }
            else
            {
                chatGui.Print(
                    $"[MjAuto] Usage: /mjauto capture <label>. Run again with no label to disarm. " +
                    $"File: {plugin.EventLogger.CaptureLogPath}");
            }
            return;
        }

        foreach (var c in label)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                chatGui.PrintError(
                    $"[MjAuto] capture label must be [a-zA-Z0-9_-] only — got '{label}'.");
                return;
            }
        }

        if (plugin.Configuration.AutomationArmed)
        {
            chatGui.PrintError(
                "[MjAuto] auto-play is ON — its dispatches would race your manual click. " +
                "Run `/mjauto off` first, then re-arm capture.");
            return;
        }

        plugin.EventLogger.ArmCapture(label);
        chatGui.Print(
            $"[MjAuto] capture armed: '{label}'. Click the action in-game once. " +
            $"Auto-disarms after one click or 60s. File: {plugin.EventLogger.CaptureLogPath}");
    }

    private void HandleVariant(string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (sub)
        {
            case "dump":
                DumpVariant();
                break;
            case "":
                chatGui.Print("[MjAuto] Usage: /mjauto variant dump");
                break;
            default:
                chatGui.PrintError(
                    $"[MjAuto] Unknown variant subcommand: '{sub}'. Known: dump.");
                break;
        }
    }

    internal unsafe void DumpVariant()
    {
        framework.RunOnFrameworkThread(() =>
        {
            if (!addon.TryGet(out var unit, out var resolvedName))
            {
                chatGui.PrintError(
                    "[MjAuto] Mahjong addon not found — seat at a table first, then retry.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            var now = DateTime.UtcNow;
            nint addonAddr = (nint)unit;

            sb.AppendLine($"# Emj variant dump — utc={now:o}");
            sb.AppendLine(
                $"# Resolved addon name: \"{resolvedName}\"  " +
                $"(candidates: {string.Join(", ", MahjongAddon.CandidateNames)})");
            sb.AppendLine($"# Addon pointer: 0x{addonAddr:X}  visible={unit->IsVisible}");
            if (unit->RootNode != null)
                sb.AppendLine(
                    $"# Root size: {unit->RootNode->Width}x{unit->RootNode->Height}");
            sb.AppendLine(
                $"# AtkValuesCount={unit->AtkValuesCount}  " +
                $"NodeListCount={unit->UldManager.NodeListCount}  " +
                $"LoadedState={unit->UldManager.LoadedState}");
            sb.AppendLine();

            sb.AppendLine("## Variant probe results");
            var selectorVariants = plugin.AddonReader.Selector.Variants;
            if (selectorVariants.Count == 0)
            {
                sb.AppendLine("  (no variants registered)");
            }
            else
            {
                foreach (var v in selectorVariants)
                {
                    bool matched = false;
                    try
                    { matched = v.Probe(unit); }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"  {v.Name,-20} THREW: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    sb.AppendLine($"  {v.Name,-20} {(matched ? "MATCH" : "miss")}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## AtkValues");
            var values = unit->AtkValues;
            int atkCount = unit->AtkValuesCount;
            if (values == null || atkCount == 0)
            {
                sb.AppendLine("  (null or empty)");
            }
            else
            {
                int cap = Math.Min(atkCount, 64);
                for (int i = 0; i < cap; i++)
                    sb.AppendLine($"  [{i,3}] {FormatAtkValue(values[i])}");
                if (atkCount > cap)
                    sb.AppendLine($"  (... {atkCount - cap} more omitted)");
            }
            sb.AppendLine();

            sb.AppendLine("## NodeList (flat)");
            var mgr = unit->UldManager;
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                {
                    sb.AppendLine($"  [{i,4}] null");
                    continue;
                }
                WriteNodeRow(sb, i, n);
            }
            sb.AppendLine();

            sb.AppendLine("## Component inner trees (visible, type >= 1000)");
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                    continue;
                if ((int)n->Type < 1000)
                    continue;
                if (!n->NodeFlags.HasFlag(NodeFlags.Visible))
                    continue;

                var compNode = (AtkComponentNode*)n;
                var comp = compNode->Component;
                if (comp == null)
                    continue;
                var subMgr = comp->UldManager;
                sb.AppendLine(
                    $"  # [{i,4}] type={n->Type} id={n->NodeId} @0x{(nint)n:X}  " +
                    $"subCount={subMgr.NodeListCount}  comp=0x{(nint)comp:X}");

                if (subMgr.NodeList == null || subMgr.NodeListCount == 0)
                    continue;
                for (int j = 0; j < subMgr.NodeListCount; j++)
                {
                    var sn = subMgr.NodeList[j];
                    if (sn == null)
                    {
                        sb.AppendLine($"    sub[{j,3}] null");
                        continue;
                    }
                    sb.Append("    ");
                    WriteNodeRow(sb, j, sn);
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Addon memory sample (+0x0400..+0x0E80)");
            byte* basePtr = (byte*)addonAddr;
            for (int off = 0x0400; off < 0x0E80; off += 16)
                AppendHexRow(sb, basePtr, off, 16);
            sb.AppendLine();

            sb.AppendLine("## AgentEmj header sample (+0x0000..+0x0200)");
            var agentModule = AgentModule.Instance();
            if (agentModule == null)
            {
                sb.AppendLine("  (AgentModule unavailable)");
            }
            else
            {
                var agent = agentModule->GetAgentByInternalId((AgentId)5);
                if (agent == null)
                {
                    sb.AppendLine("  (AgentEmj not found — GetAgentByInternalId(5) returned null)");
                }
                else
                {
                    sb.AppendLine($"  # AgentEmj @ 0x{(nint)agent:X}");
                    byte* agentPtr = (byte*)agent;
                    for (int off = 0; off < 0x0200; off += 16)
                        AppendHexRow(sb, agentPtr, off, 16);
                }
            }

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "emj-variant-dump.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print(
                $"[MjAuto] variant dump → {path}. " +
                $"Attach this file to issue #13 when reporting a new client variant.");
        });
    }

    private static unsafe string FormatAtkValue(AtkValue v)
    {
        switch (v.Type)
        {
            case ValueType.Int:
                return $"{v.Type,-14} Int={v.Int}";
            case ValueType.UInt:
                return $"{v.Type,-14} UInt={v.UInt} (0x{v.UInt:X})";
            case ValueType.Bool:
                return $"{v.Type,-14} Bool={v.Byte != 0}";
            case ValueType.String:
            case ValueType.String8:
            case ValueType.ManagedString:
                if (v.String.Value == null)
                    return $"{v.Type,-14} (null)";
                var s = v.String.ToString();
                if (s.Length > 80)
                    s = s[..80] + "...";
                return $"{v.Type,-14} \"{s.Replace("\n", "\\n")}\"";
            default:
                return $"{v.Type,-14} raw=0x{v.UInt:X}";
        }
    }

    internal unsafe void HandleSnap(string arg)
    {
        var label = arg.Trim();
        if (string.IsNullOrEmpty(label))
        {
            chatGui.PrintError(
                "[MjAuto] Usage: /mjauto snap <label>  (label: [a-zA-Z0-9_-] only)");
            return;
        }
        foreach (var c in label)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                chatGui.PrintError(
                    $"[MjAuto] snap label must be [a-zA-Z0-9_-] only — got '{label}'.");
                return;
            }
        }

        framework.RunOnFrameworkThread(() =>
        {
            var path = WriteSnapFile(label, verbose: true);
            if (path != null)
                chatGui.Print($"[MjAuto] snap '{label}' → {path}");
        });
    }

    internal void HandleAutoSnap(string arg)
    {
        var v = arg.Trim().ToLowerInvariant();
        switch (v)
        {
            case "on":
                if (autoSnapOn)
                {
                    chatGui.Print("[MjAuto] autosnap already ON.");
                    return;
                }
                autoSnapOn = true;
                autoSnapCounter = 0;
                autoSnapLastHash = 0;
                autoSnapLastMs = 0;
                plugin.AddonReader.ObservationChanged += OnAutoSnapObservation;
                chatGui.Print(
                    $"[MjAuto] autosnap ON. Hash-deduped, min gap {AutoSnapMinGapMs}ms, cap {AutoSnapMaxCount}. " +
                    $"Files: snap-auto-NNN-<ts>.txt in plugin config dir.");
                break;

            case "off":
                if (!autoSnapOn)
                {
                    chatGui.Print("[MjAuto] autosnap already OFF.");
                    return;
                }
                autoSnapOn = false;
                plugin.AddonReader.ObservationChanged -= OnAutoSnapObservation;
                chatGui.Print($"[MjAuto] autosnap OFF. Wrote {autoSnapCounter} file(s).");
                break;

            case "":
                chatGui.Print(
                    $"[MjAuto] autosnap is {(autoSnapOn ? "ON" : "OFF")} " +
                    $"(wrote {autoSnapCounter}/{AutoSnapMaxCount}).");
                break;

            default:
                chatGui.PrintError("[MjAuto] Usage: /mjauto autosnap <on|off>");
                break;
        }
    }

    private unsafe void OnAutoSnapObservation(AddonEmjObservation obs)
    {
        if (!autoSnapOn)
            return;
        if (!obs.Present || obs.Address == 0)
            return;
        if (autoSnapCounter >= AutoSnapMaxCount)
        {
            autoSnapOn = false;
            plugin.AddonReader.ObservationChanged -= OnAutoSnapObservation;
            chatGui.Print(
                $"[MjAuto] autosnap hit cap ({AutoSnapMaxCount}) — auto-disarmed. " +
                $"Toggle off then on to reset.");
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - autoSnapLastMs < AutoSnapMinGapMs)
            return;

        // FireCallback can race the refresh that triggered this observation — read
        // only what VirtualQuery says is committed, and swallow non-fatal errors so
        // one torn struct doesn't kill the autosnap session for the rest of the match.
        ulong hash;
        try
        {
            hash = ComputeAutoSnapHash(obs.Address);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[MjAuto] autosnap hash failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        if (hash == autoSnapLastHash)
            return;

        autoSnapLastHash = hash;
        autoSnapLastMs = nowMs;
        var label = $"auto-{autoSnapCounter:D3}";
        autoSnapCounter++;
        try
        {
            WriteSnapFile(label, verbose: false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[MjAuto] autosnap write '{label}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static unsafe ulong ComputeAutoSnapHash(nint addonAddr)
    {
        ulong hash = 1469598103934665603UL;
        byte* addonPtr = (byte*)addonAddr;
        int addonLen = MaxSafeReadLength(addonPtr + 0x0500, 0x3000 - 0x0500);
        for (int i = 0; i < addonLen; i++)
            hash = (hash ^ addonPtr[0x0500 + i]) * 1099511628211UL;

        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return hash;
        var agent = agentModule->GetAgentByInternalId((AgentId)5);
        if (agent == null)
            return hash;
        byte* agentPtr = (byte*)agent;
        int agentLen = MaxSafeReadLength(agentPtr, 0x3000);
        for (int i = 0; i < agentLen; i++)
            hash = (hash ^ agentPtr[i]) * 1099511628211UL;
        return hash;
    }

    // ---- VirtualQuery-based readability gate -------------------------------
    // Every byte* read in WriteSnapFile + autosnap routes through these helpers.
    // Without them, dumping past the end of a struct (or chasing a torn agent
    // pointer mid-PostRefresh) faults the game with an AccessViolationException
    // that CLR can't catch.

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern nint VirtualQuery(nint address, out MEMORY_BASIC_INFORMATION buffer, nint length);

    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_GUARD = 0x100;
    /// <summary>PAGE_READONLY|READWRITE|WRITECOPY|EXECUTE_READ|EXECUTE_READWRITE|EXECUTE_WRITECOPY.</summary>
    private const uint PAGE_READABLE_MASK = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;

    /// <summary>True if every byte in [ptr, ptr+length) is committed and readable without faulting.</summary>
    private static unsafe bool IsReadable(void* ptr, int length)
    {
        if (ptr == null || length <= 0)
            return false;
        nint addr = (nint)ptr;
        nint end = addr + length;
        while (addr < end)
        {
            if (VirtualQuery(addr, out var info, (nint)sizeof(MEMORY_BASIC_INFORMATION)) == 0)
                return false;
            if (info.State != MEM_COMMIT)
                return false;
            if ((info.Protect & PAGE_GUARD) != 0)
                return false;
            if ((info.Protect & PAGE_READABLE_MASK) == 0)
                return false;
            nint regionEnd = info.BaseAddress + info.RegionSize;
            if (regionEnd <= addr)
                return false;
            addr = regionEnd;
        }
        return true;
    }

    /// <summary>Largest prefix of [basePtr, basePtr+requested) that's safely readable, rounded down to 16 bytes for hex-row alignment.</summary>
    private static unsafe int MaxSafeReadLength(byte* basePtr, int requested)
    {
        if (basePtr == null || requested <= 0)
            return 0;
        int safe = 0;
        const int probeChunk = 0x1000;
        while (safe < requested)
        {
            int probe = Math.Min(requested - safe, probeChunk);
            if (!IsReadable(basePtr + safe, probe))
                break;
            safe += probe;
        }
        return safe & ~0xF;
    }

    /// <summary>Must be called on the framework thread.</summary>
    private unsafe string? WriteSnapFile(string label, bool verbose)
    {
        if (!addon.TryGet(out var unit, out _))
        {
            if (verbose)
                chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
            return null;
        }
        nint addonAddr = (nint)unit;

        var sb = new System.Text.StringBuilder();
        var now = DateTime.UtcNow;
        string ts = now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);

        sb.AppendLine($"# SNAP label='{label}'  utc={now:o}  addon=0x{addonAddr:X}");

        var snap = plugin.AddonReader.TryBuildSnapshot();
        if (snap != null)
        {
            sb.AppendLine(
                $"  hand={Tiles.Render(snap.Hand)}  " +
                $"wall={snap.WallRemaining}  scores=[{string.Join(",", snap.Scores)}]  " +
                $"legal={snap.Legal.Flags}");
        }
        else
        {
            sb.AppendLine("  (TryBuildSnapshot returned null — addon not visible yet)");
        }

        var atkValues = unit->AtkValues;
        int atkCount = unit->AtkValuesCount;
        int stateCode = -1;
        if (atkValues != null && atkCount > 0
            && atkValues[0].Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int)
            stateCode = atkValues[0].Int;
        sb.AppendLine($"  stateCode={stateCode}  atkValuesCount={atkCount}");

        sb.AppendLine("  -- AtkValues --");
        if (atkValues != null)
        {
            for (int i = 0; i < atkCount && i < 128; i++)
            {
                var v = atkValues[i];
                sb.Append($"  [{i,3}] {v.Type,-14} ");
                switch (v.Type)
                {
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int:
                        sb.Append($"Int={v.Int}");
                        break;
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt:
                        sb.Append($"UInt={v.UInt} (0x{v.UInt:X})");
                        break;
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool:
                        sb.Append($"Bool={v.Byte != 0}");
                        break;
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String:
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8:
                    case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ManagedString:
                        var s = v.String.Value != null
                            ? v.String.ToString() : "(null)";
                        sb.Append($"String=\"{s}\"");
                        break;
                    default:
                        sb.Append($"raw=0x{v.UInt:X}");
                        break;
                }
                sb.AppendLine();
            }
        }

        byte* addonPtr = (byte*)addonAddr;
        int addonDumpLen = MaxSafeReadLength(addonPtr, 0x6000);
        sb.AppendLine($"  -- addon @ +0x0000..+0x{addonDumpLen:X4} --");
        for (int off = 0; off < addonDumpLen; off += 16)
            AppendHexRow(sb, addonPtr, off, 16);
        if (addonDumpLen < 0x6000)
            sb.AppendLine($"  (truncated: only 0x{addonDumpLen:X} of 0x6000 committed)");

        var agentModule = AgentModule.Instance();
        AgentInterface* agent = null;
        if (agentModule != null)
        {
            agent = agentModule->GetAgentByInternalId((AgentId)5);
            if (agent != null)
            {
                byte* agentPtr = (byte*)agent;
                int agentDumpLen = MaxSafeReadLength(agentPtr, 0x3000);
                sb.AppendLine($"  -- AgentEmj @ 0x{(nint)agent:X} +0x0000..+0x{agentDumpLen:X4} --");
                for (int off = 0; off < agentDumpLen; off += 16)
                    AppendHexRow(sb, agentPtr, off, 16);
                if (agentDumpLen < 0x3000)
                    sb.AppendLine($"  (truncated: only 0x{agentDumpLen:X} of 0x3000 committed)");
            }
            else
            {
                sb.AppendLine("  -- AgentEmj unavailable (GetAgentByInternalId returned null) --");
            }
        }
        else
        {
            sb.AppendLine("  -- AgentModule unavailable --");
        }

        // Candidate-pointer walk: dereferences arbitrary pointers from agent memory.
        // Skipped for autosnap because PostRefresh can race the agent updating these
        // slots, and a torn pointer is uncatchable (AccessViolationException).
        if (verbose && agent != null)
        {
            sb.AppendLine("  -- Agent-referenced candidate structs --");
            nint* slots = (nint*)agent;
            int dumped = 0;
            var seen = new System.Collections.Generic.HashSet<nint>();
            for (int i = 1; i < 32 && dumped < 8; i++)
            {
                nint p = slots[i];
                if (p == nint.Zero)
                    continue;
                if ((ulong)p < 0x10000UL || (ulong)p > 0x0000_7FFF_FFFF_FFFFUL)
                    continue;
                if (((ulong)p & 0xF) != 0)
                    continue;
                if (!seen.Add(p))
                    continue;

                byte* pb = (byte*)p;
                int candLen = MaxSafeReadLength(pb, 0x2000);
                if (candLen < 0x100)
                    continue;

                sb.AppendLine(
                    $"  -- candidate[{i}] @ 0x{p:X}  (agent+0x{i * 8:X2})  +0x0000..+0x{candLen:X4} --");
                for (int off = 0; off < candLen; off += 16)
                    AppendHexRow(sb, pb, off, 16);
                dumped++;
            }
            if (dumped == 0)
                sb.AppendLine("  (no readable pointer candidates found in agent+0..+0x100)");
        }

        var dir = pluginInterface.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"snap-{label}-{ts}.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static unsafe void AppendHexRow(
        System.Text.StringBuilder sb, byte* basePtr, int offset, int length)
    {
        sb.Append($"  +0x{offset:X4}: ");
        for (int i = 0; i < length; i++)
        {
            sb.Append($"{basePtr[offset + i]:X2} ");
            if (i == 7)
                sb.Append(' ');
        }
        sb.Append(" |");
        for (int i = 0; i < length; i++)
        {
            byte b = basePtr[offset + i];
            sb.Append(b >= 32 && b < 127 ? (char)b : '.');
        }
        sb.AppendLine("|");
    }

    internal unsafe void HandlePoolIcons()
    {
        framework.RunOnFrameworkThread(() =>
        {
            if (!addon.TryGet(out var unit, out _))
            {
                chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# poolicons  addon=0x{(nint)unit:X}  utc={DateTime.UtcNow:o}");

            var mgr = unit->UldManager;
            if (mgr.NodeList == null)
            { sb.AppendLine("  no NodeList"); return; }

            var slotsByType = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(uint Id, nint CompNodeAddr)>>();
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                    continue;
                int t = (int)n->Type;
                if (t < 1021 || t > 1024)
                    continue;
                if (!n->NodeFlags.HasFlag(NodeFlags.Visible))
                    continue;
                var compNode = (AtkComponentNode*)n;
                if (compNode->Component == null)
                    continue;
                if (!slotsByType.TryGetValue(t, out var list))
                    slotsByType[t] = list = new();
                list.Add((n->NodeId, (nint)compNode));
            }

            int totalDecoded = 0;
            foreach (var kvp in slotsByType.OrderBy(k => k.Key))
            {
                int type = kvp.Key;
                var slots = kvp.Value;
                sb.AppendLine();
                sb.AppendLine($"## type={type}  visible={slots.Count}");

                foreach (var entry in slots)
                {
                    var compNode = (AtkComponentNode*)entry.CompNodeAddr;
                    var line = $"  slot id={entry.Id}  comp=0x{(nint)compNode->Component:X}  ";
                    var decoded = TryReadSlotIcon(compNode, out var info);
                    sb.AppendLine(line + info);
                    if (decoded)
                        totalDecoded++;
                }
            }

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "emj-poolicons.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print($"[MjAuto] poolicons → {path}  ({totalDecoded} decoded)");
        });
    }

    private static unsafe bool TryReadSlotIcon(AtkComponentNode* compNode, out string info)
    {
        info = "?";
        var comp = compNode->Component;
        if (comp == null)
        { info = "no component"; return false; }
        var inner = comp->UldManager;
        if (inner.NodeList == null)
        { info = "no inner NodeList"; return false; }

        // Tile face is Image node id=5; other ids are chrome.
        AtkImageNode* imgNode = null;
        for (int i = 0; i < inner.NodeListCount; i++)
        {
            var n = inner.NodeList[i];
            if (n == null)
                continue;
            if (n->NodeId != 5)
                continue;
            if (n->Type != NodeType.Image)
                continue;
            imgNode = (AtkImageNode*)n;
            break;
        }
        if (imgNode == null)
        { info = "no image node id=5"; return false; }

        var pl = imgNode->PartsList;
        if (!IsHeapPointer((nint)pl))
        { info = $"bad partsList=0x{(nint)pl:X}"; return false; }

        byte* nb = (byte*)imgNode;
        var imgHex = new System.Text.StringBuilder();
        for (int b = 0; b < 0x40; b++)
        {
            imgHex.Append($"{nb[b]:X2}");
            if ((b & 7) == 7)
                imgHex.Append(' ');
        }
        string imgInfo = $"imgNode=0x{(nint)imgNode:X}  pl=0x{(nint)pl:X}  partId={imgNode->PartId}  [{imgHex}]";

        if (pl->Parts == null || imgNode->PartId >= pl->PartCount)
        {
            info = $"{imgInfo}  parts={(nint)pl->Parts:X} count={pl->PartCount}";
            return false;
        }
        var part = &pl->Parts[imgNode->PartId];
        string partInfo = $"part_uv=({part->U},{part->V}) part_wh=({part->Width},{part->Height}) asset=0x{(nint)part->UldAsset:X}";

        var asset = part->UldAsset;
        if (!IsHeapPointer((nint)asset))
        { info = $"{imgInfo}  {partInfo}  bad uldAsset"; return false; }

        var atkTex = asset->AtkTexture;
        var res = atkTex.Resource;
        if (!IsHeapPointer((nint)res))
        { info = $"{imgInfo}  {partInfo}  bad texResource=0x{(nint)res:X}"; return false; }

        try
        {
            uint iconId = res->IconId;
            int tileId = (int)iconId - 76041;
            if (tileId >= 0 && tileId < Tile.Count34)
            {
                info = $"{imgInfo}  {partInfo}  iconId={iconId} → tile={tileId} ({Tile.FromId(tileId)})";
                return true;
            }
            info = $"{imgInfo}  {partInfo}  res=0x{(nint)res:X} iconId=-1";
            return true;
        }
        catch (Exception ex)
        {
            info = $"{imgInfo}  {partInfo}  read failed: {ex.GetType().Name}";
            return false;
        }
    }

    private static bool IsHeapPointer(nint p) =>
        p != nint.Zero
        && (ulong)p >= 0x10000UL
        && (ulong)p <= 0x0000_7FFF_FFFF_FFFFUL
        && ((ulong)p & 0x7) == 0;

    internal unsafe void HandlePoolSlots()
    {
        framework.RunOnFrameworkThread(() =>
        {
            if (!addon.TryGet(out var unit, out _))
            {
                chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# poolslots  addon=0x{(nint)unit:X}  utc={DateTime.UtcNow:o}");

            var mgr = unit->UldManager;
            if (mgr.NodeList == null)
            { sb.AppendLine("  no NodeList"); return; }

            var slotsByType = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(uint Id, nint Comp, float X, float Y)>>();
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                    continue;
                int t = (int)n->Type;
                if (t < 1021 || t > 1024)
                    continue;
                if (!n->NodeFlags.HasFlag(NodeFlags.Visible))
                    continue;
                var compNode = (AtkComponentNode*)n;
                if (compNode->Component == null)
                    continue;
                if (!slotsByType.TryGetValue(t, out var list))
                    slotsByType[t] = list = new();
                list.Add((n->NodeId, (nint)compNode->Component, n->X, n->Y));
            }

            const int dumpLen = 0x800;
            foreach (var kvp in slotsByType.OrderBy(k => k.Key))
            {
                int type = kvp.Key;
                var slots = kvp.Value;
                sb.AppendLine();
                sb.AppendLine($"## type={type}  visible-slot-count={slots.Count}");

                if (slots.Count == 0)
                    continue;

                var slotBytes = new byte[slots.Count][];
                for (int s = 0; s < slots.Count; s++)
                {
                    var arr = new byte[dumpLen];
                    byte* cb = (byte*)slots[s].Comp;
                    for (int j = 0; j < dumpLen; j++)
                        arr[j] = cb[j];
                    slotBytes[s] = arr;
                }

                for (int s = 0; s < slots.Count; s++)
                    sb.AppendLine($"  slot[{s}]  id={slots[s].Id}  comp=0x{slots[s].Comp:X}  xy=({slots[s].X:F0},{slots[s].Y:F0})");

                sb.AppendLine();
                sb.AppendLine("  -- MASKED-6BIT candidates: (byte & 0x3F) all-tile-range AND all-distinct --");
                int maskedCount = 0;
                for (int off = 0; off < dumpLen; off++)
                {
                    bool allInRange = true;
                    var maskedSeen = new System.Collections.Generic.HashSet<byte>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = (byte)(slotBytes[s][off] & 0x3F);
                        if (b >= Tile.Count34)
                        { allInRange = false; break; }
                        maskedSeen.Add(b);
                    }
                    if (!allInRange || maskedSeen.Count != slotBytes.Length)
                        continue;
                    maskedCount++;
                    var values = new System.Collections.Generic.List<string>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte raw = slotBytes[s][off];
                        byte masked = (byte)(raw & 0x3F);
                        values.Add($"[{s}]raw=0x{raw:X2}→{masked}({Tile.FromId(masked)})");
                    }
                    sb.AppendLine($"    +0x{off:X3}: {string.Join(" ", values)}");
                }
                sb.AppendLine($"  -- {maskedCount} masked-6bit candidates --");

                sb.AppendLine();
                sb.AppendLine("  -- STRONG candidates: all-tile-range AND all-distinct --");
                int strongCount = 0;
                for (int off = 0; off < dumpLen; off++)
                {
                    bool allInRange = true;
                    var seen = new System.Collections.Generic.HashSet<byte>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = slotBytes[s][off];
                        if (b >= Tile.Count34)
                        { allInRange = false; break; }
                        seen.Add(b);
                    }
                    if (!allInRange || seen.Count != slotBytes.Length)
                        continue;
                    strongCount++;
                    var values = new System.Collections.Generic.List<string>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = slotBytes[s][off];
                        values.Add($"[{s}]={b}({Tile.FromId(b)})");
                    }
                    sb.AppendLine($"    +0x{off:X3}: {string.Join(" ", values)}");
                }
                sb.AppendLine($"  -- {strongCount} strong candidates --");

                sb.AppendLine();
                sb.AppendLine("  -- MEDIUM candidates: all-tile-range, ≥N-1 distinct --");
                int mediumCount = 0;
                for (int off = 0; off < dumpLen; off++)
                {
                    bool allInRange = true;
                    var seen = new System.Collections.Generic.HashSet<byte>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = slotBytes[s][off];
                        if (b >= Tile.Count34)
                        { allInRange = false; break; }
                        seen.Add(b);
                    }
                    if (!allInRange || seen.Count == slotBytes.Length)
                        continue;
                    if (seen.Count < slotBytes.Length - 1)
                        continue;
                    mediumCount++;
                    var values = new System.Collections.Generic.List<string>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = slotBytes[s][off];
                        values.Add($"[{s}]={b}({Tile.FromId(b)})");
                    }
                    sb.AppendLine($"    +0x{off:X3}: {string.Join(" ", values)}");
                }
                sb.AppendLine($"  -- {mediumCount} medium candidates --");

                sb.AppendLine();
                sb.AppendLine("  -- ALL differing offsets (raw, includes pointer noise) --");
                int diffCount = 0;
                for (int off = 0; off < dumpLen; off++)
                {
                    byte first = slotBytes[0][off];
                    bool varies = false;
                    for (int s = 1; s < slotBytes.Length; s++)
                        if (slotBytes[s][off] != first)
                        { varies = true; break; }
                    if (!varies)
                        continue;
                    diffCount++;
                    var values = new System.Collections.Generic.List<string>();
                    for (int s = 0; s < slotBytes.Length; s++)
                    {
                        byte b = slotBytes[s][off];
                        string tile = b < Tile.Count34 ? $"={b}({Tile.FromId(b)})" : $"=0x{b:X2}";
                        values.Add($"[{s}]{tile}");
                    }
                    sb.AppendLine($"    +0x{off:X3}: {string.Join(" ", values)}");
                }
                sb.AppendLine($"  -- {diffCount} differing offsets total --");
            }

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "emj-poolslots.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print($"[MjAuto] poolslots → {path}  ({slotsByType.Sum(kvp => kvp.Value.Count)} visible slots)");
        });
    }

    internal unsafe void HandleHexDump(string args)
    {
        framework.RunOnFrameworkThread(() =>
        {
            var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            bool agentMode = parts.Length > 0 && parts[0].Equals("agent", StringComparison.OrdinalIgnoreCase);
            int rangeArgStart = agentMode ? 1 : 0;

            byte* basePtr;
            string label;
            int regionMax;
            if (agentMode)
            {
                var agentModule = AgentModule.Instance();
                if (agentModule == null)
                { chatGui.PrintError("[MjAuto] AgentModule unavailable."); return; }
                var agent = agentModule->GetAgentByInternalId((AgentId)5);
                if (agent == null)
                { chatGui.PrintError("[MjAuto] AgentEmj unavailable."); return; }
                basePtr = (byte*)agent;
                label = $"AgentEmj=0x{(nint)agent:X}";
                regionMax = 0x3000;
            }
            else
            {
                if (!addon.TryGet(out var unit, out _))
                {
                    chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
                    return;
                }
                basePtr = (byte*)unit;
                label = $"addon=0x{(nint)unit:X}";
                regionMax = 0x4000;
            }


            int defaultStart = agentMode ? 0x0100 : 0x0C00;
            int defaultEnd = agentMode ? 0x0900 : 0x1400;
            int start = defaultStart, end = defaultEnd;
            if (parts.Length > rangeArgStart && TryParseHex(parts[rangeArgStart], out var s))
                start = s;
            if (parts.Length > rangeArgStart + 1 && TryParseHex(parts[rangeArgStart + 1], out var e))
                end = e;
            start = Math.Clamp(start, 0, regionMax);
            end = Math.Clamp(end, start + 16, regionMax);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# hexdump  {label}  range=+0x{start:X4}..+0x{end:X4}  utc={DateTime.UtcNow:o}");
            for (int off = start; off < end; off += 16)
                AppendHexRow(sb, basePtr, off, 16);

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var fname = agentMode ? "emj-hexdump-agent.txt" : "emj-hexdump.txt";
            var path = System.IO.Path.Combine(dir, fname);
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print($"[MjAuto] hexdump → {path}  ({(end - start) / 16} rows)");
        });
    }

    internal unsafe void HandleFindTiles()
    {
        framework.RunOnFrameworkThread(() =>
        {
            if (!addon.TryGet(out var unit, out _))
            {
                chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
                return;
            }

            // Hand reader uses 76041 on Emj, 76001 on EmjL (was 76003 pre-#52); scan the known bases.
            int[] candidateBases = { 76041, 76001, 76003 };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# findtiles  utc={DateTime.UtcNow:o}");
            sb.AppendLine();
            int totalMatches = 0;

            totalMatches += ScanRegion(sb, "addon", (byte*)unit, 0x4000, candidateBases);

            var agentModule = AgentModule.Instance();
            if (agentModule != null)
            {
                var agent = agentModule->GetAgentByInternalId((AgentId)5);
                if (agent != null)
                    totalMatches += ScanRegion(sb, "AgentEmj", (byte*)agent, 0x3000, candidateBases);
                else
                    sb.AppendLine("  (AgentEmj unavailable — GetAgentByInternalId returned null)");
            }
            else
            {
                sb.AppendLine("  (AgentModule unavailable)");
            }

            if (agentModule != null)
            {
                var agent = agentModule->GetAgentByInternalId((AgentId)5);
                if (agent != null)
                {
                    nint* slots = (nint*)agent;
                    var seen = new System.Collections.Generic.HashSet<nint>();
                    int candidateCount = 0;
                    for (int i = 1; i < 32 && candidateCount < 12; i++)
                    {
                        nint p = slots[i];
                        if (p == nint.Zero)
                            continue;
                        if ((ulong)p < 0x10000UL || (ulong)p > 0x0000_7FFF_FFFF_FFFFUL)
                            continue;
                        if (((ulong)p & 0xF) != 0)
                            continue;
                        if (!seen.Add(p))
                            continue;
                        bool nonZero = false;
                        byte* pb = (byte*)p;
                        for (int j = 0; j < 16 && !nonZero; j++)
                            if (pb[j] != 0)
                                nonZero = true;
                        if (!nonZero)
                            continue;
                        totalMatches += ScanRegion(
                            sb, $"agent+0x{i * 8:X2} -> 0x{p:X}", pb, 0x2000, candidateBases);
                        candidateCount++;
                    }
                }
            }

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "emj-findtiles.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print($"[MjAuto] findtiles → {path}  ({totalMatches} matches)");
        });
    }

    private static unsafe int ScanRegion(
        System.Text.StringBuilder sb, string label, byte* basePtr, int length, int[] texBases)
    {
        sb.AppendLine($"## {label}  base=0x{(nint)basePtr:X}  length=0x{length:X}");
        int matches = 0;

        foreach (var texBase in texBases)
        {
            int forBase = 0;
            for (int off = 0; off + 4 <= length; off += 4)
            {
                int tex = *(int*)(basePtr + off);
                int tileId = tex - texBase;
                if (tileId < 0 || tileId >= Tile.Count34)
                    continue;
                forBase++;
                var tile = Tile.FromId(tileId);
                sb.AppendLine($"  i32+{texBase}  +0x{off:X4}: tex={tex} tile={tileId} ({tile})");
            }
            if (forBase > 0)
                sb.AppendLine($"  -- {forBase} matches for i32+{texBase}");
            matches += forBase;
        }

        {
            int rawHits = 0;
            for (int off = 0; off + 4 <= length; off += 4)
            {
                int v = *(int*)(basePtr + off);
                if (v < 0 || v >= Tile.Count34)
                    continue;
                rawHits++;
                if (rawHits <= 64)
                    sb.AppendLine($"  i32-raw   +0x{off:X4}: v={v} ({Tile.FromId(v)})");
            }
            if (rawHits > 64)
                sb.AppendLine($"  ... ({rawHits - 64} more raw-i32 hits suppressed)");
            if (rawHits > 0)
                sb.AppendLine($"  -- {rawHits} raw-i32 hits");
            matches += rawHits;
        }

        {
            int runStart = -1;
            int runLen = 0;
            int runHits = 0;
            var distinct = new System.Collections.Generic.HashSet<byte>();
            void EmitRun(int endExclusive)
            {
                if (runStart < 0 || runLen < 4 || distinct.Count < 3)
                    return;
                runHits++;
                var bytes = new byte[runLen];
                for (int k = 0; k < runLen; k++)
                    bytes[k] = basePtr[runStart + k];
                var rendered = string.Join(",", bytes.Select(b => $"{b}({Tile.FromId(b)})"));
                if (runHits <= 32)
                    sb.AppendLine($"  bytes     +0x{runStart:X4}..+0x{endExclusive - 1:X4} ({runLen}): {rendered}");
            }
            for (int off = 0; off < length; off++)
            {
                byte b = basePtr[off];
                if (b < Tile.Count34)
                {
                    if (runStart < 0)
                    { runStart = off; runLen = 0; distinct.Clear(); }
                    runLen++;
                    distinct.Add(b);
                }
                else
                {
                    EmitRun(off);
                    runStart = -1;
                    runLen = 0;
                }
            }
            EmitRun(length);
            if (runHits > 32)
                sb.AppendLine($"  ... ({runHits - 32} more byte-run hits suppressed)");
            if (runHits > 0)
                sb.AppendLine($"  -- {runHits} byte-runs");
            matches += runHits;
        }

        if (matches == 0)
            sb.AppendLine("  (no tile-encoded values found in this region)");
        sb.AppendLine();
        return matches;
    }

    internal unsafe void HandleWalkNodes()
    {
        framework.RunOnFrameworkThread(() =>
        {
            if (!addon.TryGet(out var unit, out _))
            {
                chatGui.PrintError("[MjAuto] Emj addon not found — open a table first.");
                return;
            }

            nint addonAddr = (nint)unit;
            var mgr = unit->UldManager;
            var sb = new System.Text.StringBuilder();
            var now = DateTime.UtcNow;
            sb.AppendLine($"# walknodes  addon=0x{addonAddr:X}  utc={now:o}");
            sb.AppendLine(
                $"  NodeListCount={mgr.NodeListCount}  ObjectsCount={mgr.ObjectCount}  " +
                $"LoadedState={mgr.LoadedState}");

            sb.AppendLine();
            sb.AppendLine("## NodeList (flat)");
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                {
                    sb.AppendLine($"  [{i,4}] null");
                    continue;
                }
                WriteNodeRow(sb, i, n);
            }

            // Custom component types (>=1000) carry their tile visuals under Component->UldManager, not the addon's flat NodeList.
            sb.AppendLine();
            sb.AppendLine("## Component inner trees (type >= 1000)");
            for (int i = 0; i < mgr.NodeListCount; i++)
            {
                var n = mgr.NodeList[i];
                if (n == null)
                    continue;
                if ((int)n->Type < 1000)
                    continue;
                var compNode = (AtkComponentNode*)n;
                var comp = compNode->Component;
                if (comp == null)
                    continue;
                var sub = comp->UldManager;
                bool visible = n->NodeFlags.HasFlag(
                    FFXIVClientStructs.FFXIV.Component.GUI.NodeFlags.Visible);
                sb.AppendLine(
                    $"  # [{i,4}] type={n->Type} id={n->NodeId} @0x{(nint)n:X}  " +
                    $"vis={(visible ? "1" : "0")}  subCount={sub.NodeListCount}  " +
                    $"comp=0x{(nint)comp:X}");

                sb.AppendLine("    -- comp bytes +0x00..+0x300 --");
                byte* cb = (byte*)comp;
                for (int off = 0; off < 0x300; off += 16)
                    AppendHexRow(sb, cb, off, 16);

                if (sub.NodeListCount == 0 || sub.NodeList == null)
                    continue;
                for (int j = 0; j < sub.NodeListCount; j++)
                {
                    var sn = sub.NodeList[j];
                    if (sn == null)
                    {
                        sb.AppendLine($"    sub[{j,3}] null");
                        continue;
                    }
                    sb.Append("    ");
                    WriteNodeRow(sb, j, sn);
                }
            }

            var dir = pluginInterface.GetPluginConfigDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "emj-nodes.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            chatGui.Print($"[MjAuto] walknodes → {path}  ({mgr.NodeListCount} nodes)");
        });
    }

    private static unsafe void WriteNodeRow(
        System.Text.StringBuilder sb, int index, AtkResNode* n)
    {
        string idxStr = index >= 0 ? $"[{index,4}]" : "     ";
        sb.Append(
            $"  {idxStr} @0x{(nint)n:X}  type={n->Type,-18}  id={n->NodeId,-5}  " +
            $"vis={(n->NodeFlags.HasFlag(FFXIVClientStructs.FFXIV.Component.GUI.NodeFlags.Visible) ? "1" : "0")}  " +
            $"xy=({n->X:F0},{n->Y:F0})  wh=({n->Width},{n->Height})");

        if (n->Type == FFXIVClientStructs.FFXIV.Component.GUI.NodeType.Image)
        {
            var img = (FFXIVClientStructs.FFXIV.Component.GUI.AtkImageNode*)n;
            sb.Append($"  partId={img->PartId}");
            var pl = img->PartsList;
            if (pl != null)
            {
                sb.Append($"  partsListId={pl->Id}  partCount={pl->PartCount}");
                if (img->PartId < pl->PartCount && pl->Parts != null)
                {
                    var part = &pl->Parts[img->PartId];
                    sb.Append($"  u/v=({part->U},{part->V})  w/h=({part->Width},{part->Height})");
                    var ui = part->UldAsset;
                    if (ui != null)
                    {
                        sb.Append($"  uldAssetId={ui->Id}");
                    }
                }
            }
        }
        else if (n->Type == FFXIVClientStructs.FFXIV.Component.GUI.NodeType.Text)
        {
            var txt = (FFXIVClientStructs.FFXIV.Component.GUI.AtkTextNode*)n;
            var s = txt->NodeText.ToString();
            if (!string.IsNullOrEmpty(s))
            {
                if (s.Length > 40)
                    s = s[..40] + "...";
                sb.Append($"  text=\"{s.Replace("\n", "\\n")}\"");
            }
        }
        sb.AppendLine();
    }

    internal void HandleLog(string arg)
    {
        var v = arg.Trim().ToLowerInvariant();
        switch (v)
        {
            case "on":
                plugin.EventLogger.Enabled = true;
                plugin.EventLogger.OpenLog();
                chatGui.Print($"[MjAuto] event logger ON. Writing to {plugin.EventLogger.LogPath}");
                break;
            case "off":
                plugin.EventLogger.Enabled = false;
                plugin.EventLogger.CloseLog();
                chatGui.Print("[MjAuto] event logger OFF.");
                break;
            case "":
                chatGui.Print(
                    $"[MjAuto] event logger is {(plugin.EventLogger.Enabled ? "ON" : "OFF")}. " +
                    $"Path: {plugin.EventLogger.LogPath}");
                break;
            default:
                chatGui.PrintError("[MjAuto] Usage: /mjauto log <on|off>");
                break;
        }
    }

    internal unsafe void DumpAtkValues()
    {
        if (!addon.TryGet(out var unit, out _))
        {
            chatGui.PrintError("[MjAuto] Emj addon not found.");
            return;
        }

        var values = unit->AtkValues;
        int count = unit->AtkValuesCount;
        if (values == null || count == 0)
        {
            chatGui.PrintError("[MjAuto] AtkValues is null or empty.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Emj AtkValues @ 0x{(nint)values:X}  count={count}  utc={DateTime.UtcNow:o}");

        for (int i = 0; i < count; i++)
        {
            var v = values[i];
            string display;
            switch (v.Type)
            {
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int:
                    display = $"Int={v.Int}";
                    break;
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt:
                    display = $"UInt={v.UInt} (0x{v.UInt:X})";
                    break;
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool:
                    display = $"Bool={v.Byte != 0}";
                    break;
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String:
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8:
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ManagedString:
                    display = $"String=\"{(v.String.Value != null ? v.String.ToString() : "(null)")}\"";
                    break;
                case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Vector:
                    display = "Vector";
                    break;
                default:
                    display = $"(type={v.Type}) raw=0x{v.UInt:X}";
                    break;
            }
            sb.AppendLine($"[{i,3}] {v.Type,-16} {display}");
        }

        var dir = pluginInterface.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "emj-atkvalues.txt");
        System.IO.File.WriteAllText(path, sb.ToString());

        chatGui.Print($"[MjAuto] wrote {count} AtkValues to {path}");
    }

    internal unsafe void DumpMemory(string args)
    {
        var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int offset = 0x238;
        int length = 0x400;
        if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0]) && !TryParseHex(parts[0], out offset))
        {
            chatGui.PrintError($"[MjAuto] bad offset '{parts[0]}'. Use hex, optional 0x prefix.");
            return;
        }
        if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]) && !TryParseHex(parts[1], out length))
        {
            chatGui.PrintError($"[MjAuto] bad length '{parts[1]}'. Use hex, optional 0x prefix.");
            return;
        }
        length = Math.Clamp(length, 1, 0x2000);

        if (!addon.TryGet(out var unit, out _))
        {
            chatGui.PrintError("[MjAuto] Emj addon not found.");
            return;
        }
        nint addr = (nint)unit;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Emj @ 0x{addr:X}  offset=0x{offset:X}  length=0x{length:X}  utc={DateTime.UtcNow:o}");

        byte* basePtr = (byte*)addr;
        for (int row = 0; row < length; row += 16)
        {
            sb.Append($"0x{offset + row:X4}: ");
            for (int i = 0; i < 16; i++)
            {
                if (row + i < length)
                    sb.Append($"{basePtr[offset + row + i]:X2} ");
                else
                    sb.Append("   ");
                if (i == 7)
                    sb.Append(' ');
            }
            sb.Append(" |");
            for (int i = 0; i < 16 && row + i < length; i++)
            {
                byte b = basePtr[offset + row + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }

        var dir = pluginInterface.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "emj-dump.txt");
        System.IO.File.WriteAllText(path, sb.ToString());

        chatGui.Print($"[MjAuto] wrote 0x{length:X} bytes @ +0x{offset:X} to {path}");
    }

    private static bool TryParseHex(string s, out int value)
    {
        if (s.StartsWith("0x") || s.StartsWith("0X"))
            s = s[2..];
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    internal unsafe void DumpAddons(string filter)
    {
        var stage = AtkStage.Instance();
        if (stage == null)
        {
            chatGui.PrintError("[MjAuto] AtkStage not available (not in game?)");
            return;
        }

        var unitManagers = stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        var filterLower = filter?.Trim().ToLowerInvariant() ?? string.Empty;

        int count = 0;
        for (int i = 0; i < unitManagers.Count; i++)
        {
            var unit = unitManagers.Entries[i].Value;
            if (unit == null)
                continue;

            var name = unit->NameString;
            if (!string.IsNullOrEmpty(filterLower) &&
                !name.ToLowerInvariant().Contains(filterLower))
                continue;

            chatGui.Print(
                $"[MjAuto] {name,-24} @ 0x{(nint)unit:X}  vis={unit->IsVisible}");
            count++;
        }
        chatGui.Print($"[MjAuto] {count} addon(s) {(string.IsNullOrEmpty(filterLower) ? "total" : $"matching \"{filter}\"")}.");
    }

}
