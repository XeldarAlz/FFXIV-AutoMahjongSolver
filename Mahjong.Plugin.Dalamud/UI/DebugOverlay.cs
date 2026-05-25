using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.UI.DebugTabs;

namespace Mahjong.Plugin.Dalamud.UI;

/// <summary>Developer console window — thin host for the per-tab classes under UI/DebugTabs.</summary>
public sealed class DebugOverlay : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly DevConsoleContext ctx;
    private readonly StatusTab statusTab;
    private readonly AddonTab addonTab;
    private readonly ActionsTab actionsTab;
    private readonly BugReportTab bugReportTab;
    private readonly ReProbesTab reProbesTab;
    private readonly DiagnosticsTab diagnosticsTab;

    public DebugOverlay(
        Plugin plugin, IFramework framework, ICommandManager commandManager, MahjongAddon addon)
        : base("Doman Mahjong · Developer###domanmahjong-debug")
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(commandManager);
        ArgumentNullException.ThrowIfNull(addon);
        this.plugin = plugin;
        ctx = new DevConsoleContext(plugin, framework, commandManager, addon);
        statusTab = new StatusTab(ctx);
        addonTab = new AddonTab(ctx);
        actionsTab = new ActionsTab(ctx);
        bugReportTab = new BugReportTab(ctx);
        reProbesTab = new ReProbesTab(ctx);
        diagnosticsTab = new DiagnosticsTab(ctx);

        Size = new Vector2(620, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 480),
            MaximumSize = new Vector2(1100, 2000),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        using var _s = Theme.PushWindowStyle();

        DrawHeaderCard();
        ImGui.Dummy(new Vector2(0, 4));

        if (ImGui.BeginTabBar("##debug-tabs"))
        {
            DrawTab("Status", statusTab.Draw);
            DrawTab("Addon", addonTab.Draw);
            DrawTab("Actions", actionsTab.Draw);
            DrawTab("Bug report", bugReportTab.Draw);
            DrawTab("Reverse Engineering", reProbesTab.Draw);
            DrawTab("Diagnostics", diagnosticsTab.Draw);
            ImGui.EndTabBar();
        }

        if (!string.IsNullOrEmpty(ctx.LastToast))
        {
            ImGui.Dummy(new Vector2(0, 6));
            DevHelpers.DrawToast(ctx.LastToast);
        }
    }

    private static void DrawTab(string label, Action body)
    {
        if (!ImGui.BeginTabItem(label))
            return;
        ImGui.Dummy(new Vector2(0, 4));
        body();
        ImGui.EndTabItem();
    }

    private void DrawHeaderCard()
    {
        var cfg = plugin.Configuration;
        if (cfg.TosAccepted)
            return;

        using (Theme.BeginCard("debug-header"))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
            ImGui.TextWrapped("Automation is disabled until the ToS notice is acknowledged in the main window.");
            ImGui.PopStyleColor();
            if (ImGui.Button("Acknowledge and enable"))
                plugin.ConfigService.Update(c => c with { TosAccepted = true });
        }
    }
}
