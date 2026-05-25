using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>RE primitives: walknodes, findtiles, poolslots/icons, hexdump, dumpmem, atkvalues, addons.</summary>
internal sealed class ReProbesTab
{
    private readonly DevConsoleContext ctx;
    private string hexStart = "0x0C00";
    private string hexEnd = "0x1400";
    private bool hexAgent;
    private string memOffset = "0x0238";
    private string memLength = "0x0400";
    private string addonsFilter = "";

    public ReProbesTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        var cmd = ctx.Plugin.MjAutoCommand;

        using (Theme.BeginCard("re-tree"))
        {
            Theme.SectionHeader("Addon tree & memory scan");
            Theme.Subtle("walknodes: every UI node in the addon. findtiles: scan memory for tile-encoded values. atkvalues: the addon's parameter array.");
            if (ImGui.Button("Walk Nodes"))
            {
                cmd.HandleWalkNodes();
                ctx.LastToast = "walknodes queued → emj-nodes.txt";
            }
            ImGui.SameLine(0, 3);
            DevHelpers.CopyPathButton("emj-nodes.txt", "nodes");
            ImGui.SameLine(0, 10);
            if (ImGui.Button("Find Tiles"))
            {
                cmd.HandleFindTiles();
                ctx.LastToast = "findtiles queued → emj-findtiles.txt";
            }
            ImGui.SameLine(0, 3);
            DevHelpers.CopyPathButton("emj-findtiles.txt", "findtiles");
            ImGui.SameLine(0, 10);
            if (ImGui.Button("Atk Values"))
            {
                cmd.DumpAtkValues();
                ctx.LastToast = "atkvalues → emj-atkvalues.txt";
            }
            ImGui.SameLine(0, 3);
            DevHelpers.CopyPathButton("emj-atkvalues.txt", "atkvalues");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("re-pool"))
        {
            Theme.SectionHeader("Discard-pool slot diff");
            Theme.Subtle("Finds which byte inside each pool-tile slot encodes the tile id, by comparing slots with different tiles. Used to locate fields when a client patch moves them.");
            if (ImGui.Button("Pool Slots"))
            {
                cmd.HandlePoolSlots();
                ctx.LastToast = "poolslots queued → emj-poolslots.txt";
            }
            ImGui.SameLine(0, 3);
            DevHelpers.CopyPathButton("emj-poolslots.txt", "poolslots");
            ImGui.SameLine(0, 10);
            if (ImGui.Button("Pool Icons"))
            {
                cmd.HandlePoolIcons();
                ctx.LastToast = "poolicons queued → emj-poolicons.txt";
            }
            ImGui.SameLine(0, 3);
            DevHelpers.CopyPathButton("emj-poolicons.txt", "poolicons");
            Theme.Subtle("Walks types 1021..1024 and diffs candidate tile bytes across visible slots.");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("re-hex"))
        {
            Theme.SectionHeader("Hex dump");
            Theme.Subtle("Raw bytes of the addon (or AgentEmj) over a chosen range. Defaults target the current best-guess region.");
            ImGui.Checkbox("Agent (vs addon)", ref hexAgent);
            ImGui.SetNextItemWidth(120);
            ImGui.InputText("start##hex", ref hexStart, 16);
            ImGui.SameLine(0, 8);
            ImGui.SetNextItemWidth(120);
            ImGui.InputText("end##hex", ref hexEnd, 16);

            bool startOk = DevHelpers.TryParseHex(hexStart, out _);
            bool endOk = DevHelpers.TryParseHex(hexEnd, out _);
            using (DevHelpers.Disable(!(startOk && endOk)))
            {
                if (ImGui.Button("Hex Dump"))
                {
                    var args = (hexAgent ? "agent " : "") + $"{hexStart} {hexEnd}";
                    cmd.HandleHexDump(args);
                    ctx.LastToast = $"hexdump {args.Trim()} → emj-hexdump{(hexAgent ? "-agent" : "")}.txt";
                }
            }
            ImGui.SameLine(0, 6);
            DevHelpers.CopyPathButton(hexAgent ? "emj-hexdump-agent.txt" : "emj-hexdump.txt", "hex");
            if (!startOk || !endOk)
                Theme.Subtle("Use hex with optional 0x prefix.");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("re-mem"))
        {
            Theme.SectionHeader("Addon memory (offset + length)");
            Theme.Subtle("Same idea as Hex dump but addon-only and written to emj-dump.txt. Convenient for fixed-offset structures.");
            ImGui.SetNextItemWidth(120);
            ImGui.InputText("offset##mem", ref memOffset, 16);
            ImGui.SameLine(0, 8);
            ImGui.SetNextItemWidth(120);
            ImGui.InputText("length##mem", ref memLength, 16);

            bool offOk = DevHelpers.TryParseHex(memOffset, out _);
            bool lenOk = DevHelpers.TryParseHex(memLength, out _);
            using (DevHelpers.Disable(!(offOk && lenOk)))
            {
                if (ImGui.Button("Dump Memory"))
                {
                    cmd.DumpMemory($"{memOffset} {memLength}");
                    ctx.LastToast = $"dumpmem +{memOffset} len {memLength} → emj-dump.txt";
                }
            }
            ImGui.SameLine(0, 6);
            DevHelpers.CopyPathButton("emj-dump.txt", "dump");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("re-addons"))
        {
            Theme.SectionHeader("List loaded addons");
            Theme.Subtle("Print every game UI addon currently loaded. Substring filter; empty = all. Used to confirm the mahjong addon name on your client.");
            ImGui.SetNextItemWidth(220);
            ImGui.InputText("filter##addons", ref addonsFilter, 64);
            ImGui.SameLine(0, 8);
            if (ImGui.Button("List Addons"))
            {
                cmd.DumpAddons(addonsFilter);
                ctx.LastToast = "addons → chat";
            }
        }
    }
}
