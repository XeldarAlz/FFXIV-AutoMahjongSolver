using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Components;

internal static class StatTile
{
    private const float TooltipWrapWidth = 260f;

    public static void Draw(FontAwesomeIcon icon, string value, string caption, Vector4 accent, Vector2 size,
        string? subtitle = null, string? tooltip = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, accent, 0.10f)), Styling.CardRounding);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.BorderDim), Styling.CardRounding, ImDrawFlags.None, 1f);
        dl.AddRectFilled(origin, new Vector2(end.X, origin.Y + 3f * s), ImGui.GetColorU32(accent), Styling.CardRounding, ImDrawFlags.RoundCornersTop);

        var pad = 10f * s;
        var iconStr = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(iconStr);

        var topY = origin.Y + 9f * s;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, topY));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, accent))
            ImGui.TextUnformatted(iconStr);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad + iconSize.X + 6f * s, topY + (iconSize.Y - ImGui.GetTextLineHeight()) * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted(caption);

        ImGui.SetWindowFontScale(1.45f);
        var valSize = ImGui.CalcTextSize(value);
        var valTop = end.Y - valSize.Y - 8f * s;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, valTop));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            ImGui.TextUnformatted(value);
        ImGui.SetWindowFontScale(1f);

        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.SetWindowFontScale(0.82f);
            var subSize = ImGui.CalcTextSize(subtitle);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + pad + valSize.X + 6f * s, valTop + valSize.Y - subSize.Y - 2f * s));
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted(subtitle);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.PushTextWrapPos(TooltipWrapWidth * s);
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextSecondary))
                    ImGui.TextUnformatted(tooltip);
                ImGui.PopTextWrapPos();
            }
        }
    }
}
