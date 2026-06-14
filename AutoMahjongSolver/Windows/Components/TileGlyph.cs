using AutoMahjongSolver.Core.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Components;

/// <summary>
/// Renders a Doman Mahjong tile by drawing the game's own tile icon (ui/icon/076000/0760xx) — the
/// same texture the board uses — so a suggested tile is visually identical to the one in the player's
/// hand and can't be misread. The icon id is the inverse of <see cref="EmjTiles"/>'s decode, red fives
/// included. A drop shadow and an optional glow frame the tile; a neutral tile holds the slot for the
/// frame or two before the texture finishes loading (never a stand-in symbol).
/// </summary>
internal static class TileGlyph
{
    public const float Aspect = 0.72f; // width / height

    public static float Width(float heightPx) => heightPx * Aspect;

    /// <summary>Lay out a tile at the cursor (height in logical px), advancing by a Dummy.</summary>
    public static void Draw(int tile, float height, bool aka = false, Vector4? glow = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        var heightPx = height * s;
        var widthPx = Width(heightPx);
        var shadow = 3f * s;

        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(widthPx, heightPx);

        if (glow is { } g)
        {
            var dl = ImGui.GetWindowDrawList();
            for (var i = 3; i >= 1; i--)
            {
                var grow = i * 3f * s;
                dl.AddRectFilled(min - new Vector2(grow, grow), max + new Vector2(grow, grow),
                    ImGui.GetColorU32(Styling.WithAlpha(g, 0.06f * i)), Styling.TileRounding * s + grow);
            }
        }

        Paint(min, max, tile, aka);

        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(new Vector2(widthPx + shadow, heightPx + shadow));
    }

    /// <summary>Paint a tile into an explicit rectangle (hero card, ranking table, custom placement).</summary>
    public static void Paint(Vector2 min, Vector2 max, int tile, bool aka = false, float alpha = 1f)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var rounding = Styling.TileRounding * s;
        var shadowOffset = new Vector2(1.5f * s, 2.5f * s);
        var shadowColor = ImGui.GetColorU32(Styling.WithAlpha(Styling.TileShadow, Styling.TileShadow.W * alpha));

        int iconId = EmjTiles.ToIconId(tile, aka);
        if (iconId >= 0 &&
            Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup((uint)iconId)).TryGetWrap(out var wrap, out _))
        {
            // Fit the icon to its real aspect so it's never distorted; we don't assume the icon shape.
            var (iMin, iMax) = Contain(min, max, wrap.Width, wrap.Height);
            dl.AddRectFilled(iMin + shadowOffset, iMax + shadowOffset, shadowColor, rounding);
            dl.AddImageRounded(wrap.Handle, iMin, iMax, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Styling.WithAlpha(Styling.White, alpha)), rounding, ImDrawFlags.RoundCornersAll);
            return;
        }

        dl.AddRectFilled(min + shadowOffset, max + shadowOffset, shadowColor, rounding);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(Styling.WithAlpha(Styling.TileFace, alpha)), rounding);
        dl.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(Styling.TileEdge, alpha * 0.9f)),
            rounding, ImDrawFlags.None, 1f * s);
    }

    /// <summary>The largest rectangle of the texture's aspect that fits inside [min,max], centered.</summary>
    private static (Vector2 Min, Vector2 Max) Contain(Vector2 min, Vector2 max, float texW, float texH)
    {
        var box = max - min;
        if (texW <= 0 || texH <= 0 || box.X <= 0 || box.Y <= 0)
            return (min, max);

        var scale = MathF.Min(box.X / texW, box.Y / texH);
        var size = new Vector2(texW * scale, texH * scale);
        var offset = (box - size) * 0.5f;
        return (min + offset, min + offset + size);
    }
}
