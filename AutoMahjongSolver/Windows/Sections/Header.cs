using AutoMahjongSolver.Core;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

internal static class Header
{
    public static void Draw(Plugin plugin, AutoPlayController auto)
    {
        var running = auto.Running;
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.AlignTextToFramePadding();
        var frameHeight = ImGui.GetFrameHeight();
        var origin = ImGui.GetCursorScreenPos();
        var dotRadius = 3.5f * scale;
        var dotColor = running
            ? Styling.PulseColor(Styling.Accent, Styling.AccentSoft, Styling.PulseCalm)
            : Styling.WithAlpha(Styling.Accent, 0.5f);
        drawList.AddCircleFilled(new Vector2(origin.X + dotRadius, origin.Y + frameHeight * 0.5f), dotRadius, ImGui.GetColorU32(dotColor));

        ImGui.Dummy(new Vector2(dotRadius * 2 + 7 * scale, frameHeight));
        ImGui.SameLine(0, 0);
        ImGui.AlignTextToFramePadding();
        var title = running
            ? (auto.Mode == PlayMode.Auto ? "Auto-play is running" : "Suggestion mode is on")
            : Greeting();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
        {
            ImGui.TextUnformatted(title);
        }

        DrawIconCluster(plugin, frameHeight);
        Styling.HairlineRule(2f, 2f);
    }

    private static string Greeting()
    {
        return DateTime.Now.Hour switch
        {
            >= 5 and < 12 => "Good morning, ready to read some tiles?",
            >= 12 and < 17 => "Good afternoon, ready for some mahjong runs?",
            >= 17 and < 22 => "Good evening, let's read some tiles.",
            _ => "Late night mahjong session? Let's go.",
        };
    }

    private static void DrawIconCluster(Plugin plugin, float buttonSize)
    {
        const int buttonCount = 2;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var total = buttonCount * buttonSize + (buttonCount - 1) * spacing;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - total);

        if (IconButton.Draw(FontAwesomeIcon.InfoCircle, "##amjs_about", "About", null, buttonSize))
        {
            plugin.ToggleAboutUi();
        }

        ImGui.SameLine();
        if (IconButton.Draw(FontAwesomeIcon.Cog, "##amjs_settings", "Settings", null, buttonSize))
        {
            plugin.ToggleConfigUi();
        }
    }
}
