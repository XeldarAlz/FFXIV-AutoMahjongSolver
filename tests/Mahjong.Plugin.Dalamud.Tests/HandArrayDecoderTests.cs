using System.Linq;
using Mahjong.Core;
using Mahjong.Plugin.Dalamud.GameState.Variants;

namespace Mahjong.Plugin.Dalamud.Tests;

public class HandArrayDecoderTests
{
    // Matches data/layouts/emj.json.
    private const int EmjTextureBase = 76041;

    /// <summary>Reproduces the stuck-state hand-array captured 2026-05-25: 10 tiles in [00..09], zeros in [10..12], post-pon claim at [13].</summary>
    private static int[] PostPonGapRaw() => new[]
    {
        76055, 76057, 76060, 76060, 76062, 76064, 76065, 76066, 76073, 76073,
        0, 0, 0, 76073,
    };

    [Fact]
    public void ReadHand_post_pon_gap_layout_counts_eleven_tiles()
    {
        var (tiles, _) = HandArrayDecoder.ReadHand(PostPonGapRaw(), EmjTextureBase);
        Assert.Equal(11, tiles.Count);
    }

    [Fact]
    public void ReadHand_post_pon_gap_layout_includes_slot_13_tile()
    {
        var (tiles, _) = HandArrayDecoder.ReadHand(PostPonGapRaw(), EmjTextureBase);
        // Three 6z (id=32): two in slots 8/9, one parked at slot 13. Zero-terminating would have skipped slot 13.
        Assert.Equal(3, tiles.FindAll(t => t.Id == 32).Count);
    }

    [Fact]
    public void ReadHand_post_pon_count_satisfies_discard_gate()
    {
        var (tiles, _) = HandArrayDecoder.ReadHand(PostPonGapRaw(), EmjTextureBase);
        // BuildLegalActions grants Discard only when hand.Count % 3 == 2; the bug was hand=10 (10 % 3 == 1 → no Discard → stuck).
        Assert.Equal(2, tiles.Count % 3);
    }

    [Fact]
    public void ReadHand_contiguous_thirteen_tile_hand_unchanged()
    {
        var raw = new int[14]
        {
            76041, 76042, 76043, 76044, 76045, 76046, 76047, 76048, 76049, 76050, 76051, 76052, 76053,
            0,
        };
        var (tiles, _) = HandArrayDecoder.ReadHand(raw, EmjTextureBase);
        Assert.Equal(13, tiles.Count);
    }

    [Fact]
    public void ReadHand_empty_array_returns_no_tiles()
    {
        var raw = new int[14];
        var (tiles, aka) = HandArrayDecoder.ReadHand(raw, EmjTextureBase);
        Assert.Empty(tiles);
        Assert.Equal(0, aka);
    }

    [Fact]
    public void ReadHand_decodes_akadora_aliases()
    {
        // textureBase+34 = 5m red, +35 = 5p red, +36 = 5s red.
        var raw = new int[14] { 76041 + 34, 76041 + 35, 76041 + 36, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var (tiles, aka) = HandArrayDecoder.ReadHand(raw, EmjTextureBase);
        Assert.Equal(3, tiles.Count);
        Assert.Equal(4, tiles[0].Id);   // 5m
        Assert.Equal(13, tiles[1].Id);  // 5p
        Assert.Equal(22, tiles[2].Id);  // 5s
        Assert.Equal(3, aka);
    }

    [Fact]
    public void ReadHand_skips_out_of_range_raw_values()
    {
        // 999999 well past any valid tile alias; should be ignored, not added.
        var raw = new int[14] { 76055, 999999, 76057, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var (tiles, _) = HandArrayDecoder.ReadHand(raw, EmjTextureBase);
        Assert.Equal(2, tiles.Count);
    }

    [Fact]
    public void FindAddonSlot_post_pon_layout_returns_slot_13_for_claim_tile()
    {
        // 6z (id=32) appears at addon slots 8, 9, 13. Slot 13 (last-drawn) wins.
        int slot = HandArrayDecoder.FindAddonSlot(PostPonGapRaw(), EmjTextureBase, targetTileId: 32);
        Assert.Equal(13, slot);
    }

    [Fact]
    public void FindAddonSlot_returns_lowest_addon_slot_when_slot_13_does_not_match()
    {
        // 6p (id=14) only at slot 0; 6z at slot 13 doesn't match the target.
        int slot = HandArrayDecoder.FindAddonSlot(PostPonGapRaw(), EmjTextureBase, targetTileId: 14);
        Assert.Equal(0, slot);
    }

    [Fact]
    public void FindAddonSlot_returns_minus_one_when_tile_absent()
    {
        int slot = HandArrayDecoder.FindAddonSlot(PostPonGapRaw(), EmjTextureBase, targetTileId: 0);
        Assert.Equal(-1, slot);
    }

    [Fact]
    public void FindAddonSlot_decodes_akadora_alias_to_canonical_id()
    {
        // Slot 5 holds the 5m-red alias (textureBase + 34); FindAddonSlot for canonical id=4 must locate it.
        var raw = new int[14] { 76055, 0, 0, 0, 0, 76041 + 34, 0, 0, 0, 0, 0, 0, 0, 0 };
        int slot = HandArrayDecoder.FindAddonSlot(raw, EmjTextureBase, targetTileId: 4);
        Assert.Equal(5, slot);
    }

    // --- Self-calibrating base (issue #52): guard against a future tile-ID shift silently mis-reading every tile ---
    // EU capture snap-auto-029-20260526: 13-tile hand whose array holds 1m/2m at raw 76001/76002. Under a stale
    // base of 76003 those underflow and drop, collapsing the hand to 11 tiles. ResolveTextureBase must recover 76001.
    private static int[] Issue52StaleBaseRaw() => new[]
    {
        76001, 76002, 76010, 76011, 76015, 76016, 76018, 76020, 76021, 76021, 76021, 76024, 76033, 0,
    };

    // EU capture snap-auto-001-20260526: 14-tile hand whose lowest raw is 76004, so a stale 76003 drops nothing and
    // mis-reads silently. Decoded at the correct 76001 it must be all-valid, proving no false retune fires.
    private static int[] Issue52SilentShiftRaw() => new[]
    {
        76004, 76004, 76004, 76005, 76007, 76007, 76012, 76016, 76016, 76017, 76022, 76024, 76026, 76015,
    };

    [Fact]
    public void ResolveTextureBase_recovers_shifted_base_when_configured_drops_tiles()
    {
        int resolved = HandArrayDecoder.ResolveTextureBase(Issue52StaleBaseRaw(), configuredBase: 76003, out bool shifted);
        Assert.True(shifted);
        Assert.Equal(76001, resolved);
    }

    [Fact]
    public void ResolveTextureBase_keeps_correct_base_for_full_hand()
    {
        int resolved = HandArrayDecoder.ResolveTextureBase(Issue52SilentShiftRaw(), configuredBase: 76001, out bool shifted);
        Assert.False(shifted);
        Assert.Equal(76001, resolved);
    }

    [Fact]
    public void ResolveTextureBase_does_not_retune_on_a_single_junk_slot()
    {
        // One garbage int no base in range can decode must not drag the whole array onto a wrong base.
        var raw = new int[14] { 76001, 76002, 76010, 76011, 999999, 76016, 76018, 76020, 76021, 0, 0, 0, 0, 0 };
        int resolved = HandArrayDecoder.ResolveTextureBase(raw, configuredBase: 76001, out bool shifted);
        Assert.False(shifted);
        Assert.Equal(76001, resolved);
    }

    [Fact]
    public void ResolveTextureBase_ignores_sparse_frames()
    {
        // Too few populated slots to retune safely; stay on the configured base even though it drops one.
        var raw = new int[14] { 76001, 76002, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        int resolved = HandArrayDecoder.ResolveTextureBase(raw, configuredBase: 76003, out bool shifted);
        Assert.False(shifted);
        Assert.Equal(76003, resolved);
    }

    [Fact]
    public void ReadHand_with_recovered_base_decodes_full_thirteen_tile_hand()
    {
        var raw = Issue52StaleBaseRaw();
        int resolved = HandArrayDecoder.ResolveTextureBase(raw, configuredBase: 76003, out _);
        var (tiles, _) = HandArrayDecoder.ReadHand(raw, resolved);
        // 12m 12679p 23336s 6z — the 1m/2m a stale base would have dropped are back.
        int[] expected = { 0, 1, 9, 10, 14, 15, 17, 19, 20, 20, 20, 23, 32 };
        Assert.Equal(expected, tiles.Select(t => (int)t.Id).OrderBy(id => id).ToArray());
    }
}
