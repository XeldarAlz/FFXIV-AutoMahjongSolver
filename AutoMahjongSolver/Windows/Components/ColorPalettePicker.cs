using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoMahjongSolver.Windows.Components;

/// <summary>A row of clickable colour swatches; the selected one wears a bright ring.</summary>
internal static class ColorPalettePicker
{
    private const float SwatchSize = 17f;
    private const float Gap = 6f;

    /// <summary>Unscaled control height, for vertically centring the row in a settings row.</summary>
    public const float Height = SwatchSize;

    /// <summary>Unscaled width of the swatch row, for right-aligning it in a settings row.</summary>
    public static float Width(int count) => count <= 0 ? 0f : count * SwatchSize + (count - 1) * Gap;

    public static bool Draw(string id, IReadOnlyList<Vector4> colors, int selected, out int picked)
    {
        picked = selected;
        var scale = ImGuiHelpers.GlobalScale;
        var swatch = SwatchSize * scale;
        var step = (SwatchSize + Gap) * scale;
        var rounding = 4f * scale;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var changed = false;

        for (var i = 0; i < colors.Count; i++)
        {
            var min = origin + new Vector2(i * step, 0f);
            var max = min + new Vector2(swatch, swatch);

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"{id}_{i}", new Vector2(swatch, swatch));
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked())
            {
                picked = i;
                changed = true;
            }

            draw.AddRectFilled(min + new Vector2(1f, 1.5f) * scale, max + new Vector2(1f, 1.5f) * scale,
                ImGui.GetColorU32(Styling.WithAlpha(Styling.TileShadow, 0.5f)), rounding);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(colors[i]), rounding);
            draw.AddRectFilled(min, new Vector2(max.X, min.Y + swatch * 0.4f),
                ImGui.GetColorU32(Styling.WithAlpha(Styling.White, 0.12f)), rounding, ImDrawFlags.RoundCornersTop);

            var selectedSwatch = i == selected;
            var ringColor = selectedSwatch
                ? Styling.White
                : Styling.WithAlpha(Styling.White, hovered ? 0.6f : 0.18f);
            var grow = selectedSwatch ? new Vector2(1.5f, 1.5f) * scale : Vector2.Zero;
            draw.AddRect(min - grow, max + grow, ImGui.GetColorU32(ringColor), rounding + (selectedSwatch ? 1.5f * scale : 0f),
                ImDrawFlags.None, selectedSwatch ? 2f * scale : 1f * scale);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(MathF.Max(0f, colors.Count * step - Gap * scale), swatch));
        return changed;
    }
}
