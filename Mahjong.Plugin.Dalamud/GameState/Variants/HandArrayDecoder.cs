using System;
using System.Collections.Generic;
using Mahjong.Core;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

/// <summary>Pure helpers for decoding the addon hand-array. Scans the full array (post-call layouts park the claimed tile at slot 13 with [10..12] empty).</summary>
internal static class HandArrayDecoder
{
    /// <summary>34-id decode of a raw addon int; out <paramref name="isRed"/> when the raw matches the akadora aliases 5m*/5p*/5s*. Returns -1 for zero/unknown.</summary>
    public static int DecodeTileId(int raw, int textureBase, out bool isRed)
    {
        isRed = false;
        if (raw == 0)
            return -1;
        int idx = raw - textureBase;
        if (idx >= 0 && idx < Tile.Count34)
            return idx;
        switch (idx)
        {
            case 34: isRed = true; return 4;
            case 35: isRed = true; return 13;
            case 36: isRed = true; return 22;
            default: return -1;
        }
    }

    public static (List<Tile> Tiles, int Akadora) ReadHand(ReadOnlySpan<int> rawSlots, int textureBase)
    {
        var tiles = new List<Tile>(rawSlots.Length);
        int akadora = 0;
        for (int i = 0; i < rawSlots.Length; i++)
        {
            int tileId = DecodeTileId(rawSlots[i], textureBase, out bool isRed);
            if (tileId < 0)
                continue;
            tiles.Add(Tile.FromId(tileId));
            if (isRed)
                akadora++;
        }
        return (tiles, akadora);
    }

    /// <summary>Addon slot whose raw decodes to <paramref name="targetTileId"/>. Prefers slot 13 (last-drawn), else lowest match. -1 if not found.</summary>
    public static int FindAddonSlot(ReadOnlySpan<int> rawSlots, int textureBase, int targetTileId)
    {
        if (rawSlots.Length > 13
            && DecodeTileId(rawSlots[13], textureBase, out _) == targetTileId)
            return 13;
        for (int i = 0; i < rawSlots.Length; i++)
        {
            if (i == 13)
                continue;
            if (DecodeTileId(rawSlots[i], textureBase, out _) == targetTileId)
                return i;
        }
        return -1;
    }
}
