using AutoMahjongSolver.Core;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

/// <summary>The two-segment Suggestions / Auto-play selector, shared by the idle and running panels.</summary>
internal static class ModeSwitch
{
    private const float Height = 34f;

    public static void Draw(AutoPlayController auto, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 6f * scale;
        var segWidth = (width - gap) * 0.5f;
        var size = new Vector2(segWidth, Height * scale);

        Styling.CenterNextItem(width);
        if (SegmentButton.Draw("##mode_suggest", FontAwesomeIcon.Lightbulb, "Suggestions", Styling.AccentBlue,
                auto.Mode == PlayMode.Suggest, disabled: false, size,
                "Show the best move each turn — you play it yourself. No inputs are sent."))
        {
            auto.SetMode(PlayMode.Suggest);
        }

        ImGui.SameLine(0, gap);
        if (SegmentButton.Draw("##mode_auto", FontAwesomeIcon.Robot, "Auto-play", Styling.Accent,
                auto.Mode == PlayMode.Auto, disabled: false, size,
                "The bot discards, declares tsumo/ron and passes calls for you. Casual tables only."))
        {
            auto.SetMode(PlayMode.Auto);
        }
    }
}
