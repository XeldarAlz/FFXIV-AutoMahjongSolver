using System.Numerics;
using AutoMahjongSolver.Core.Game;
using AutoMahjongSolver.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoMahjongSolver.Windows;

/// <summary>
/// In Suggest mode, paints a marker over the recommended discard tile directly on the live Doman
/// Mahjong board, so the player can read the move without looking at the cockpit window. Read-only:
/// it only draws on ImGui's foreground layer and never fires a game input, matching Suggest mode's
/// no-input contract. Reuses a <see cref="LiveBoard"/> so the highlighted tile is exactly the one the
/// cockpit's <see cref="SuggestPanel"/> recommends.
/// </summary>
internal sealed class SuggestionOverlay
{
    private readonly Plugin plugin;
    private readonly LiveBoard board = new();

    public SuggestionOverlay(Plugin plugin) => this.plugin = plugin;

    public void Draw()
    {
        if (!plugin.AutoPlay.Running || plugin.AutoPlay.Mode != PlayMode.Suggest || !Plugin.Cfg.HighlightSuggestedTile)
            return;

        board.Refresh();

        if (!board.Present || board.Phase != BoardPhase.OurTurn || board.HandComplete || board.BestDiscard < 0)
            return;

        int slot = PreferredSlot(board.Snapshot, board.BestDiscard);
        if (slot < 0 || !EmjBoardReader.TryGetHandSlotRect(slot, out Vector2 min, out Vector2 max))
            return;

        DrawHighlight(min, max);
    }

    /// <summary>The hand slot to cut: a plain copy of the suggested tile over a red five, matching the actuator's preference.</summary>
    private static int PreferredSlot(in HandSnapshot snap, int tile)
    {
        if (snap.SlotTiles is null)
            return -1;

        int akaSlot = -1;
        for (int slot = 0; slot < snap.SlotTiles.Length; slot++)
        {
            if (snap.SlotTiles[slot] != tile)
                continue;
            if (!EmjTiles.IsAkaIcon(snap.SlotIcons[slot]))
                return slot;
            akaSlot = slot;
        }

        return akaSlot;
    }

    private static void DrawHighlight(Vector2 min, Vector2 max)
    {
        OverlayTheme.Palette palette = OverlayTheme.Resolve(Plugin.Cfg.HighlightColor);
        switch (Plugin.Cfg.HighlightStyle)
        {
            case HighlightStyle.Arrow:
                DrawArrow(min, max, palette);
                break;
            case HighlightStyle.Glow:
                DrawGlow(min, max, palette);
                break;
            default:
                DrawBrackets(min, max, palette);
                break;
        }
    }

    /// <summary>Targeting-reticle corner brackets over a faint tile tint, with a soft outer aura.</summary>
    private static void DrawBrackets(Vector2 min, Vector2 max, OverlayTheme.Palette palette)
    {
        var draw = ImGui.GetForegroundDrawList();
        float scale = ImGuiHelpers.GlobalScale;
        float pulse = Styling.Pulse(Styling.PulseCalm);

        (min, max) = Pad(min, max, scale, 0.07f, 0.05f);
        float w = max.X - min.X, h = max.Y - min.Y;
        float thick = MathF.Max(2.6f * scale, h * 0.05f);
        float len = MathF.Min(w, h) * 0.34f;
        float rounding = Styling.TileRounding * scale * 1.5f;

        for (int i = 3; i >= 1; i--)
        {
            float grow = i * 2.4f * scale;
            draw.AddRect(min - new Vector2(grow), max + new Vector2(grow),
                ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.07f * i * (0.55f + 0.45f * pulse))),
                rounding + grow, ImDrawFlags.None, thick * 0.7f);
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.10f + 0.06f * pulse)), rounding);
        draw.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.32f)), rounding, ImDrawFlags.None, thick * 0.6f);

        uint bright = ImGui.GetColorU32(Styling.WithAlpha(palette.Bright, 0.85f + 0.15f * pulse));
        uint shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f));
        var off = new Vector2(scale, scale);

        void Corner(Vector2 c, Vector2 dir)
        {
            var hEnd = c + new Vector2(dir.X * len, 0f);
            var vEnd = c + new Vector2(0f, dir.Y * len);
            draw.AddLine(c + off, hEnd + off, shadow, thick);
            draw.AddLine(c + off, vEnd + off, shadow, thick);
            draw.AddLine(c, hEnd, bright, thick);
            draw.AddLine(c, vEnd, bright, thick);
        }

        Corner(min, new Vector2(1f, 1f));
        Corner(new Vector2(max.X, min.Y), new Vector2(-1f, 1f));
        Corner(new Vector2(min.X, max.Y), new Vector2(1f, -1f));
        Corner(max, new Vector2(-1f, -1f));
    }

    /// <summary>A filled arrow bobbing above the tile, pointing down at it, plus a thin tile outline.</summary>
    private static void DrawArrow(Vector2 min, Vector2 max, OverlayTheme.Palette palette)
    {
        var draw = ImGui.GetForegroundDrawList();
        float scale = ImGuiHelpers.GlobalScale;
        float bob = Styling.Pulse(Styling.PulseMedium);

        (min, max) = Pad(min, max, scale, 0.06f, 0.04f);
        float w = max.X - min.X, h = max.Y - min.Y;
        float rounding = Styling.TileRounding * scale * 1.5f;

        draw.AddRectFilled(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.12f)), rounding);
        draw.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Bright, 0.8f)), rounding,
            ImDrawFlags.None, MathF.Max(2f * scale, h * 0.045f));

        float aw = MathF.Max(18f * scale, w * 0.72f);
        float ah = aw * 0.66f;
        float gap = 6f * scale + bob * h * 0.16f;
        float cx = (min.X + max.X) * 0.5f;
        var tip = new Vector2(cx, min.Y - gap);
        var left = new Vector2(cx - aw * 0.5f, tip.Y - ah);
        var right = new Vector2(cx + aw * 0.5f, tip.Y - ah);
        var shadowOff = new Vector2(1.5f * scale, 2f * scale);

        draw.AddTriangleFilled(tip + shadowOff, left + shadowOff, right + shadowOff,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
        draw.AddTriangleFilled(tip, left, right, ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.96f)));
        draw.AddTriangle(tip, left, right, ImGui.GetColorU32(palette.Bright), 2f * scale);
    }

    /// <summary>A soft, layered pulsing halo around a strong tile tint.</summary>
    private static void DrawGlow(Vector2 min, Vector2 max, OverlayTheme.Palette palette)
    {
        var draw = ImGui.GetForegroundDrawList();
        float scale = ImGuiHelpers.GlobalScale;
        float pulse = Styling.Pulse(Styling.PulseBreath);

        (min, max) = Pad(min, max, scale, 0.05f, 0.04f);
        float h = max.Y - min.Y;
        float rounding = Styling.TileRounding * scale * 1.5f;

        for (int i = 5; i >= 1; i--)
        {
            float grow = i * 3f * scale;
            draw.AddRectFilled(min - new Vector2(grow), max + new Vector2(grow),
                ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.05f * i * (0.5f + 0.5f * pulse))),
                rounding + grow);
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Primary, 0.28f + 0.12f * pulse)), rounding);
        draw.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(palette.Bright, 0.85f + 0.15f * pulse)), rounding,
            ImDrawFlags.None, MathF.Max(2f * scale, h * 0.05f));
    }

    /// <summary>Grow a tile rect outward so the highlight frames the tile instead of sitting on its edges.</summary>
    private static (Vector2 Min, Vector2 Max) Pad(Vector2 min, Vector2 max, float scale, float fracX, float fracY)
    {
        var pad = new Vector2(
            MathF.Max(2f * scale, (max.X - min.X) * fracX),
            MathF.Max(2f * scale, (max.Y - min.Y) * fracY));
        return (min - pad, max + pad);
    }
}
