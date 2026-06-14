using AutoMahjongSolver.Core;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

internal static class IdlePanel
{
    public static void Draw(AutoPlayController auto, LiveBoard board)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var auto_ = auto.Mode == PlayMode.Auto;

        Styling.VSpace(14);
        DrawStatusPill(board);
        Styling.VSpace(16);

        ModeSwitch.Draw(auto, 320f * scale);
        Styling.VSpace(16);

        if (DrawPlayTile(scale))
        {
            auto.SetRunning(true);
        }

        Styling.VSpace(8);
        Styling.TextCentered(auto_ ? "START AUTO-PLAY" : "START SUGGESTIONS", Styling.AccentSoft, 1.0f);
        Styling.VSpace(10);
        Styling.TextCentered(auto_
            ? "Plays Doman Mahjong for you on casual tables."
            : "Reads your hand and shows the best move — you play it.", Styling.TextSecondary);
        Styling.VSpace(2);
        Styling.TextCentered(auto_
            ? "Discards  ·  tsumo / ron  ·  auto-pass  ·  auto-advance"
            : "Best discard  ·  when to tsumo / ron  ·  no inputs sent", Styling.TextMuted, 0.9f);

        if (!board.Present)
        {
            Styling.VSpace(16);
            DrawArmedHint(scale, auto_);
        }
    }

    private static void DrawStatusPill(LiveBoard board)
    {
        var (text, accent, pulse) = board.Present
            ? ("Match in progress", Styling.AccentMint, true)
            : ("No match open", Styling.TextMuted, false);

        Styling.CenterNextItem(Chip.Measure(text, dot: true).X);
        Chip.Draw(text, accent, dot: true, pulse: pulse);
    }

    /// <summary>A standing ivory tile carved with a jade play mark, glowing on its felt. Click to deal.</summary>
    private static bool DrawPlayTile(float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var tileHeight = 108f * scale;
        var tileWidth = tileHeight * TileGlyph.Aspect;
        var startScreen = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var centerX = startScreen.X + availableWidth * 0.5f;
        var restTop = startScreen.Y + 14f * scale;

        var restMin = new Vector2(centerX - tileWidth * 0.5f, restTop);
        var restMax = restMin + new Vector2(tileWidth, tileHeight);
        var hovered = ImGui.IsMouseHoveringRect(restMin, restMax);

        var ringCenter = new Vector2(centerX, restTop + tileHeight * 0.5f);
        var halo = 0.5f + 0.45f * Styling.Pulse(Styling.PulseBreath) + (hovered ? 0.7f : 0f);
        ProgressRing.Glow(ringCenter, tileHeight * 0.62f, Styling.Accent, halo);

        var lift = hovered ? 5f * scale : 0f;
        var min = restMin - new Vector2(0, lift);
        var max = restMax - new Vector2(0, lift);
        var rounding = Styling.TileRounding * scale * 1.4f;

        var shadowOffset = new Vector2(2f * scale, (4f + (hovered ? 3f : 0f)) * scale);
        drawList.AddRectFilled(min + shadowOffset, max + shadowOffset,
            ImGui.GetColorU32(Styling.WithAlpha(Styling.TileShadow, 0.45f)), rounding);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Styling.TileFace), rounding);
        drawList.AddRectFilled(min, new Vector2(max.X, min.Y + tileHeight * 0.16f),
            ImGui.GetColorU32(Styling.TileFaceTop), rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRect(min, max, ImGui.GetColorU32(hovered ? Styling.AccentSoft : Styling.TileEdge),
            rounding, ImDrawFlags.None, (hovered ? 2f : 1.2f) * scale);

        var faceCenter = new Vector2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f);
        var t = tileHeight * 0.20f;
        var p1 = faceCenter + new Vector2(-t * 0.55f, -t);
        var p2 = faceCenter + new Vector2(-t * 0.55f, t);
        var p3 = faceCenter + new Vector2(t * 0.85f, 0f);
        var shadowNudge = new Vector2(0f, 1.5f * scale);
        drawList.AddTriangleFilled(p1 + shadowNudge, p2 + shadowNudge, p3 + shadowNudge,
            ImGui.GetColorU32(Styling.WithAlpha(Styling.Accent, 0.35f)));
        drawList.AddTriangleFilled(p1, p2, p3, ImGui.GetColorU32(hovered ? Styling.AccentSoft : Styling.Accent));

        ImGui.SetCursorScreenPos(startScreen);
        ImGui.Dummy(new Vector2(availableWidth, tileHeight + 24f * scale));

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    private static void DrawArmedHint(float scale, bool auto)
    {
        var text = auto
            ? "Open a Doman Mahjong match and it takes over from there"
            : "Open a Doman Mahjong match and it starts calling the moves";
        var iconString = FontAwesomeIcon.Lightbulb.ToIconString();
        var color = Styling.AccentAmberSoft;
        var gap = 6f * scale;

        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconString);
        }

        var totalWidth = iconSize.X + gap + ImGui.CalcTextSize(text).X;
        Styling.CenterNextItem(totalWidth);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(iconString);
        }

        ImGui.SameLine(0, gap);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(text);
        }
    }
}
