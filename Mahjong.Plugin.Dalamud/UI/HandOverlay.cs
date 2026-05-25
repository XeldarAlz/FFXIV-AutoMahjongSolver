using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Policy;

namespace Mahjong.Plugin.Dalamud.UI;

/// <summary>Locates hand tiles by geometry — walk visible nodes, cluster by Y, take the tightest horizontal row of expected length.</summary>
public sealed class HandOverlay : IDisposable
{
    private const float MinTileWidth = 28f;
    private const float MaxTileWidth = 120f;
    private const float MinTileHeight = 45f;
    private const float MaxTileHeight = 160f;

    private const float MaxRowYSpread = 12f;

    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly MahjongAddon addon;
    private bool disposed;

    /// <summary>Dev console toggle: outline every detected tile rect, not just the picked slot.</summary>
    public bool DebugDrawAllRects { get; set; }

    public HandOverlay(Plugin plugin, IDalamudPluginInterface pluginInterface, MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(addon);
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.addon = addon;
        pluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        pluginInterface.UiBuilder.Draw -= Draw;
    }

    private unsafe void Draw()
    {
        var cfg = plugin.Configuration;
        bool prodEnabled = cfg.TosAccepted && cfg.ShowInGameHighlight && cfg.AutomationArmed && cfg.SuggestionOnly;
        if (!DebugDrawAllRects && !prodEnabled)
            return;

        if (!addon.TryGet(out var unit, out _))
            return;
        if (!unit->IsVisible)
            return;

        var viewportOffset = ImGui.GetMainViewport().Pos;
        var candidates = CollectTileCandidates(unit);

        if (DebugDrawAllRects)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var r = candidates[i];
                r.Pos += viewportOffset;
                DrawDebugOutline(r, i);
            }
        }

        if (!prodEnabled)
            return;

        var snap = plugin.Aggregator.Latest;
        if (snap is null || snap.Hand.Count < 2)
            return;

        var rects = PickHandRowFromCandidates(candidates, snap.Hand.Count);
        if (rects is null)
            return;

        var choice = plugin.Aggregator.LastChoice;
        if (choice?.DiscardTile is null)
            return;
        int slot = InputDispatcher.FindSlotOfTile(choice.DiscardTile.Value, snap.Hand);
        if (slot < 0 || slot >= rects.Count)
            return;

        var rect = rects[slot];
        rect.Pos += viewportOffset;
        bool isDrawnTile = slot == snap.Hand.Count - 1;

        var color = (isDrawnTile ? cfg.HighlightColorTsumogiri : cfg.HighlightColorDiscard).ToVector3();
        float intensity = Math.Clamp(cfg.HighlightIntensity, 0.4f, 1.6f);
        var dl = ImGui.GetForegroundDrawList();

        switch (cfg.HighlightStyle)
        {
            case HighlightStyle.Arrow:
                DrawHighlightArrow(dl, rect, color, intensity, isDrawnTile);
                break;
            default:
                DrawHighlightNeonGlow(dl, rect, color, intensity);
                break;
        }
    }

    private static void DrawDebugOutline((Vector2 Pos, Vector2 Size) rect, int index)
    {
        var dl = ImGui.GetForegroundDrawList();
        var min = rect.Pos - new Vector2(1, 1);
        var max = rect.Pos + rect.Size + new Vector2(1, 1);
        dl.AddRect(min, max, Theme.Pack(Theme.Info, 0.8f), 2f, ImDrawFlags.None, 1.5f);
        dl.AddText(new Vector2(min.X + 2, min.Y + 1), Theme.Pack(Theme.Info), index.ToString());
    }

    /// <summary>Every visible node whose dimensions fit a tile, in NodeList iteration order; no row-finding heuristic applied.</summary>
    private static unsafe List<(Vector2 Pos, Vector2 Size)> CollectTileCandidates(AtkUnitBase* unit)
    {
        // Parent-chain walk already includes root position and scale — do NOT add unit->X/Y or multiply by unit->Scale on top.
        var result = new List<(Vector2 Pos, Vector2 Size)>(32);
        var uld = unit->UldManager;
        if (uld.NodeList == null || uld.NodeListCount <= 0)
            return result;

        for (int i = 0; i < uld.NodeListCount; i++)
        {
            var n = uld.NodeList[i];
            if (n == null || !n->IsVisible())
                continue;

            float w = n->Width;
            float h = n->Height;
            if (w < MinTileWidth || w > MaxTileWidth)
                continue;
            if (h < MinTileHeight || h > MaxTileHeight)
                continue;
            if (w > h)
                continue;

            AbsolutePosition(n, out float nx, out float ny, out float sx, out float sy);
            result.Add((new Vector2(nx, ny), new Vector2(w * sx, h * sy)));
        }

        return result;
    }

    /// <summary>From the candidate pool, take the tightest Y-cluster of <paramref name="expected"/> tiles, then sort left-to-right.</summary>
    private static List<(Vector2 Pos, Vector2 Size)>? PickHandRowFromCandidates(List<(Vector2 Pos, Vector2 Size)> candidates, int expected)
    {
        if (candidates.Count < expected)
            return null;

        candidates.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));

        int bestStart = -1;
        float bestSpan = float.MaxValue;
        for (int i = 0; i + expected <= candidates.Count; i++)
        {
            float span = candidates[i + expected - 1].Pos.Y - candidates[i].Pos.Y;
            if (span < bestSpan)
            {
                bestSpan = span;
                bestStart = i;
            }
        }

        if (bestStart < 0 || bestSpan > MaxRowYSpread)
            return null;

        var selected = new List<(Vector2 Pos, Vector2 Size)>(expected);
        for (int i = bestStart; i < bestStart + expected; i++)
            selected.Add(candidates[i]);
        selected.Sort((a, b) => a.Pos.X.CompareTo(b.Pos.X));
        return selected;
    }

    /// <summary>Walks parent chain — result is game-window-local (before multi-viewport desktop offset) and already includes root node position.</summary>
    private static unsafe void AbsolutePosition(AtkResNode* node, out float x, out float y, out float scaleX, out float scaleY)
    {
        x = 0;
        y = 0;
        scaleX = 1f;
        scaleY = 1f;
        var cur = node;
        while (cur != null)
        {
            x = cur->X + x * cur->ScaleX;
            y = cur->Y + y * cur->ScaleY;
            scaleX *= cur->ScaleX;
            scaleY *= cur->ScaleY;
            cur = cur->ParentNode;
        }
    }

    /// <summary>Sine-eased pulse with a higher floor than Theme.Pulse so the overlay never fades to faint.</summary>
    private static float OverlayPulse(float period = 1.4f, float lo = 0.78f, float hi = 1.0f)
    {
        float t = (float)((DateTime.UtcNow.TimeOfDay.TotalSeconds % period) / period);
        float s = 0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f);
        return lo + (hi - lo) * s;
    }

    private static float ArrowBounce(float period = 0.9f, float amplitude = 5f)
    {
        float t = (float)((DateTime.UtcNow.TimeOfDay.TotalSeconds % period) / period);
        return amplitude * (0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f));
    }

    private static uint Pack(Vector3 rgb, float alpha)
        => Theme.Pack(new Vector4(rgb.X, rgb.Y, rgb.Z, Math.Clamp(alpha, 0f, 1f)));

    internal static void DrawHighlightNeonGlow(ImDrawListPtr dl, (Vector2 Pos, Vector2 Size) rect, Vector3 color, float intensity)
    {
        float pulse = OverlayPulse() * intensity;

        var min = rect.Pos - new Vector2(2, 2);
        var max = rect.Pos + rect.Size + new Vector2(2, 2);

        // Multi-ring outer glow: 4 expanding rings with decreasing alpha.
        for (int i = 4; i >= 1; i--)
        {
            float expand = i * 2.5f;
            float alpha = pulse * (0.42f / i);
            dl.AddRect(
                min - new Vector2(expand, expand),
                max + new Vector2(expand, expand),
                Pack(color, alpha),
                6f + expand, ImDrawFlags.None, 2f);
        }

        // Subtle inner fill so the tile reads as "active".
        dl.AddRectFilled(min, max, Pack(color, pulse * 0.18f), 6f);

        // Solid bright inner border.
        dl.AddRect(min, max, Pack(color, pulse), 6f, ImDrawFlags.None, 2.5f);

        // L-shaped corner brackets that pulse inward with the beat.
        float bracketLen = 10f + 2f * pulse;
        float bracketOff = 4f;
        float thick = 2.5f;
        uint bracket = Pack(color, pulse + 0.05f);
        DrawCornerBracket(dl, new Vector2(min.X - bracketOff, min.Y - bracketOff), bracketLen, +1, +1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(max.X + bracketOff, min.Y - bracketOff), bracketLen, -1, +1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(min.X - bracketOff, max.Y + bracketOff), bracketLen, +1, -1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(max.X + bracketOff, max.Y + bracketOff), bracketLen, -1, -1, thick, bracket);

        // Bouncing arrow above the tile.
        float cx = (min.X + max.X) * 0.5f;
        float bounce = ArrowBounce();
        float tipY = min.Y - 8f - bounce;
        float baseY = tipY - 18f;
        uint arrowFill = Pack(color, pulse);
        uint arrowShadow = Theme.Pack(Theme.TileShadow, 0.6f);
        dl.AddTriangleFilled(
            new Vector2(cx - 13f, baseY + 2f),
            new Vector2(cx + 13f, baseY + 2f),
            new Vector2(cx, tipY + 2f),
            arrowShadow);
        dl.AddTriangleFilled(
            new Vector2(cx - 13f, baseY),
            new Vector2(cx + 13f, baseY),
            new Vector2(cx, tipY),
            arrowFill);
    }

    private static void DrawCornerBracket(ImDrawListPtr dl, Vector2 origin, float length, int dirX, int dirY, float thickness, uint color)
    {
        var hEnd = new Vector2(origin.X + length * dirX, origin.Y);
        var vEnd = new Vector2(origin.X, origin.Y + length * dirY);
        dl.AddLine(origin, hEnd, color, thickness);
        dl.AddLine(origin, vEnd, color, thickness);
    }

    internal static void DrawHighlightArrow(ImDrawListPtr dl, (Vector2 Pos, Vector2 Size) rect, Vector3 color, float intensity, bool isDrawnTile)
    {
        float pulse = OverlayPulse(1.1f, 0.82f, 1.0f) * intensity;

        var min = rect.Pos - new Vector2(2, 2);
        var max = rect.Pos + rect.Size + new Vector2(2, 2);

        // Minimal tile treatment: thin outline so the user still sees which tile, but the art isn't covered.
        dl.AddRect(min, max, Pack(color, pulse * 0.9f), 6f, ImDrawFlags.None, 2f);

        // Big bouncing arrow with a label pill above the tile.
        string label = isDrawnTile ? "TSUMOGIRI" : "DISCARD";
        var textSize = ImGui.CalcTextSize(label);

        float cx = (min.X + max.X) * 0.5f;
        float bounce = ArrowBounce(0.85f, 6f);

        // Arrow geometry (large, filled triangle).
        float arrowHalfWidth = 18f;
        float arrowHeight = 22f;
        float tipY = min.Y - 8f - bounce;
        float arrowTopY = tipY - arrowHeight;

        // Label pill sits above the arrow.
        float pillPadX = 10f;
        float pillPadY = 4f;
        float pillW = textSize.X + pillPadX * 2f;
        float pillH = textSize.Y + pillPadY * 2f;
        var pillMin = new Vector2(cx - pillW * 0.5f, arrowTopY - pillH - 2f);
        var pillMax = pillMin + new Vector2(pillW, pillH);

        // Shadow drop.
        uint shadow = Theme.Pack(Theme.TileShadow, 0.7f);
        dl.AddRectFilled(pillMin + new Vector2(1, 2), pillMax + new Vector2(1, 2), shadow, pillH * 0.5f);
        dl.AddTriangleFilled(
            new Vector2(cx - arrowHalfWidth + 1, arrowTopY + 2),
            new Vector2(cx + arrowHalfWidth + 1, arrowTopY + 2),
            new Vector2(cx + 1, tipY + 2),
            shadow);

        // Filled pill.
        dl.AddRectFilled(pillMin, pillMax, Pack(color, pulse), pillH * 0.5f);
        dl.AddText(pillMin + new Vector2(pillPadX, pillPadY), Theme.Pack(new Vector4(0.07f, 0.08f, 0.10f, 1f)), label);

        // Filled arrow.
        dl.AddTriangleFilled(
            new Vector2(cx - arrowHalfWidth, arrowTopY),
            new Vector2(cx + arrowHalfWidth, arrowTopY),
            new Vector2(cx, tipY),
            Pack(color, pulse));
    }

}
