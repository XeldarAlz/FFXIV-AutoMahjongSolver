using AutoMahjongSolver.Core;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

/// <summary>
/// Shared chrome for the engaged cockpit — status row, mode switch and stop — routing the body to
/// <see cref="SuggestPanel"/> (tile-rich coach) or <see cref="AutoPanel"/> (tile-free autopilot).
/// </summary>
internal static class RunningPanel
{
    public static void Draw(AutoPlayController auto, LiveBoard board)
    {
        DrawStatusRow(board);
        Styling.HairlineRule(6f, 8f);
        ModeSwitch.Draw(auto, ImGui.GetContentRegionAvail().X);
        Styling.HairlineRule(8f, 10f);

        if (board.Phase == BoardPhase.NoMatch)
        {
            DrawWaiting(auto);
        }
        else if (auto.Mode == PlayMode.Auto)
        {
            AutoPanel.Draw(auto, board);
        }
        else
        {
            SuggestPanel.Draw(board);
        }

        Styling.HairlineRule(12f, 12f);
        DrawStop(auto);
    }

    // ---- Status -----------------------------------------------------------

    private static void DrawStatusRow(LiveBoard board)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.AlignTextToFramePadding();
        var frameHeight = ImGui.GetFrameHeight();
        var origin = ImGui.GetCursorScreenPos();
        var dotRadius = 3.5f * scale;
        var dotColor = board.Present
            ? Styling.PulseColor(Styling.AccentMint, Styling.AccentSoft, Styling.PulseCalm)
            : Styling.WithAlpha(Styling.TextMuted, 0.6f);
        drawList.AddCircleFilled(new Vector2(origin.X + dotRadius, origin.Y + frameHeight * 0.5f), dotRadius, ImGui.GetColorU32(dotColor));

        ImGui.Dummy(new Vector2(dotRadius * 2 + 7 * scale, frameHeight));
        ImGui.SameLine(0, 0);
        ImGui.AlignTextToFramePadding();

        var (label, color) = PhaseLabel(board.Phase);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);
    }

    private static (string Label, Vector4 Color) PhaseLabel(BoardPhase phase) => phase switch
    {
        BoardPhase.OurTurn      => ("Your turn", Styling.AccentSoft),
        BoardPhase.CallOffered  => ("A call is offered", Styling.AccentAmberSoft),
        BoardPhase.BetweenHands => ("Between hands", Styling.AccentBlueSoft),
        BoardPhase.Watching     => ("Watching the table", Styling.TextDim),
        _                       => ("Waiting for a match", Styling.TextMuted),
    };

    // ---- Waiting ----------------------------------------------------------

    private static void DrawWaiting(AutoPlayController auto)
    {
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(20);
        DrawTileWall(scale);
        Styling.VSpace(14);
        Styling.TextCentered("Waiting for a Doman Mahjong match", Styling.TextSecondary, 1.05f);
        Styling.VSpace(2);
        Styling.TextCentered("armed and ready — it starts the moment a match opens", Styling.TextMuted, 0.9f);

        // Between matches the auto session keeps its scoreboard so a long grind reads as one run.
        if (auto.Mode == PlayMode.Auto && auto.Stats.HasActivity)
        {
            Styling.VSpace(16);
            AutoPanel.DrawSessionStats(auto.Stats);
        }
    }

    /// <summary>A short stack of face-down jade tiles — the wall you draw from.</summary>
    private static void DrawTileWall(float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var tileHeight = 38f * scale;
        var tileWidth = TileGlyph.Width(tileHeight);
        const int count = 5;
        var overlap = tileWidth * 0.42f;
        var span = tileWidth + (count - 1) * overlap;

        var startScreen = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var startX = startScreen.X + (availableWidth - span) * 0.5f;
        var topY = startScreen.Y;
        var rounding = Styling.TileRounding * scale;
        var pulse = 0.4f + 0.4f * Styling.Pulse(Styling.PulseBreath);

        var glowCenter = new Vector2(startX + span * 0.5f, topY + tileHeight * 0.5f);
        ProgressRing.Glow(glowCenter, span * 0.55f, Styling.Accent, pulse);

        for (var i = 0; i < count; i++)
        {
            var min = new Vector2(startX + i * overlap, topY - (i % 2 == 0 ? 1.5f * scale : 0f));
            var max = min + new Vector2(tileWidth, tileHeight);
            drawList.AddRectFilled(min + new Vector2(1.5f * scale, 2f * scale), max + new Vector2(1.5f * scale, 2f * scale),
                ImGui.GetColorU32(Styling.WithAlpha(Styling.TileShadow, 0.4f)), rounding);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Styling.TileBack), rounding);
            drawList.AddRectFilled(min, new Vector2(max.X, min.Y + tileHeight * 0.16f),
                ImGui.GetColorU32(Styling.Lighten(Styling.TileBack, 0.18f)), rounding, ImDrawFlags.RoundCornersTop);
            drawList.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentSoft, 0.45f)), rounding, ImDrawFlags.None, 1f * scale);
        }

        ImGui.SetCursorScreenPos(startScreen);
        ImGui.Dummy(new Vector2(availableWidth, tileHeight + 6f * scale));
    }

    // ---- Stop -------------------------------------------------------------

    private static void DrawStop(AutoPlayController auto)
    {
        var acting = auto.Mode == PlayMode.Auto;
        var width = 220f * ImGuiHelpers.GlobalScale;
        Styling.CenterNextItem(width);
        if (PrimaryButton.Draw(acting ? "Stop auto-play" : "Stop suggestions", Styling.AccentRose, true, width))
        {
            auto.SetRunning(false);
        }

        Styling.VSpace(6);
        Styling.TextCentered(acting ? "or type  /amjs auto off" : "or type  /amjs suggest off", Styling.TextMuted, 0.85f);
    }
}
