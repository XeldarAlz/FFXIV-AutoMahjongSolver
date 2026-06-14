using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections.Config;

internal static class SettingsControls
{
    public const float ToggleWidth = 38f;
    public const float RowSliderWidth = 180f;
    public const float RowComboWidth = 170f;

    private const float RangeDragWidth = 62f;
    private const float RangeDashSlot = 14f;
    private const float SecondsDragSpeed = 0.02f;

    public static float RangeInlineWidth()
        => RangeDragWidth * 2f + RangeDashSlot;

    public static void DrawToggle(Configuration cfg, Func<bool> getter, Action<bool> setter)
    {
        var value = getter();
        if (ToggleSwitch.Draw(ref value))
        {
            setter(value);
            cfg.SaveDebounced();
        }
    }

    public static void DrawIntSlider(Configuration cfg, string id, Func<int> getter, Action<int> setter,
        int minimum, int maximum, string format = "%d", float width = RowSliderWidth)
    {
        var value = getter();
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
        using (PushFrameColors())
        {
            if (ImGui.SliderInt(id, ref value, minimum, maximum, format))
            {
                setter(Math.Clamp(value, minimum, maximum));
                cfg.SaveDebounced();
            }
        }
    }

    /// <summary>A min–max range edited in seconds (one decimal) but stored as integer milliseconds.</summary>
    public static void DrawSecondsRange(Configuration cfg, string minId, string maxId,
        Func<int> getMinMs, Action<int> setMinMs, Func<int> getMaxMs, Action<int> setMaxMs,
        float maxSeconds)
    {
        using var colors = PushFrameColors();

        DrawSecondsBound(cfg, minId, getMinMs, setMinMs, maxSeconds,
            onChanged: ms => { if (ms > getMaxMs()) setMaxMs(ms); });

        DrawRangeDash();

        DrawSecondsBound(cfg, maxId, getMaxMs, setMaxMs, maxSeconds,
            onChanged: ms => { if (ms < getMinMs()) setMinMs(ms); });
    }

    private static void DrawSecondsBound(Configuration cfg, string id, Func<int> getMs, Action<int> setMs,
        float maxSeconds, Action<int> onChanged)
    {
        var seconds = getMs() / 1000f;
        ImGui.SetNextItemWidth(RangeDragWidth * ImGuiHelpers.GlobalScale);
        if (!ImGui.DragFloat(id, ref seconds, SecondsDragSpeed, 0f, maxSeconds, "%.1f s"))
        {
            return;
        }

        var ms = (int)MathF.Round(Math.Clamp(seconds, 0f, maxSeconds) * 10f) * 100;
        setMs(ms);
        onChanged(ms);
        cfg.SaveDebounced();
    }

    private static void DrawRangeDash()
    {
        var dashSlot = RangeDashSlot * ImGuiHelpers.GlobalScale;

        ImGui.SameLine(0f, 0f);
        var slotOrigin = ImGui.GetCursorScreenPos();
        var dashSize = ImGui.CalcTextSize("–");
        ImGui.SetCursorScreenPos(slotOrigin + new Vector2((dashSlot - dashSize.X) * 0.5f, (ImGui.GetFrameHeight() - dashSize.Y) * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted("–");
        }

        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorScreenPos(slotOrigin with { X = slotOrigin.X + dashSlot });
    }

    private static IDisposable PushFrameColors()
        => ImRaii.PushColor(ImGuiCol.SliderGrab, Styling.Accent)
            .Push(ImGuiCol.SliderGrabActive, Styling.AccentSoft)
            .Push(ImGuiCol.FrameBg, Styling.SliderBg)
            .Push(ImGuiCol.FrameBgHovered, Styling.CardBgHover)
            .Push(ImGuiCol.FrameBgActive, Styling.CardBgHover);

    internal static class Choices
    {
        public readonly record struct Choice(string Name, string Detail);

        private const float PopupWidth = 320f;
        private const float ItemPaddingX = 6f;
        private const float ItemPaddingY = 5f;
        private const float NameDetailGap = 2f;

        public static void DrawCombo(string id, Choice[] options, int selected, Action<int> onSelect,
            float width = RowComboWidth)
        {
            var scale = ImGuiHelpers.GlobalScale;
            ImGui.SetNextItemWidth(width * scale);
            ImGui.SetNextWindowSizeConstraints(new Vector2(PopupWidth * scale, 0f), new Vector2(PopupWidth * scale, 600f * scale));

            using var frameColors = PushFrameColors();
            using var combo = ImRaii.Combo(id, options[selected].Name);
            if (!combo)
            {
                return;
            }

            for (var optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                if (DrawItem(id, options[optionIndex], optionIndex, optionIndex == selected))
                {
                    onSelect(optionIndex);
                }
            }
        }

        private static bool DrawItem(string comboId, Choice option, int optionIndex, bool selected)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var paddingX = ItemPaddingX * scale;
            var paddingY = ItemPaddingY * scale;
            var nameDetailGap = NameDetailGap * scale;
            var lineHeight = ImGui.GetTextLineHeight();
            var wrapWidth = ImGui.GetContentRegionAvail().X - paddingX * 2f;

            var detailSize = ImGui.CalcTextSize(option.Detail, false, wrapWidth);
            var itemHeight = paddingY * 2f + lineHeight + nameDetailGap + detailSize.Y;

            var itemOrigin = ImGui.GetCursorScreenPos();
            var clicked = ImGui.Selectable($"##{comboId}_opt{optionIndex}", selected,
                ImGuiSelectableFlags.None, new Vector2(0f, itemHeight));

            var drawList = ImGui.GetWindowDrawList();
            var nameColor = selected ? Styling.AccentSoft : Styling.TextStrong;
            drawList.AddText(itemOrigin + new Vector2(paddingX, paddingY), ImGui.GetColorU32(nameColor), option.Name);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                itemOrigin + new Vector2(paddingX, paddingY + lineHeight + nameDetailGap),
                ImGui.GetColorU32(Styling.TextMuted), option.Detail, wrapWidth);

            return clicked;
        }
    }
}
