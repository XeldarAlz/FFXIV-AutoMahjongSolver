using System;
using System.Buffers.Binary;
using Mahjong.Core;
using Mahjong.Plugin.Game.Variants;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

/// <summary>Builds a synthetic addon-memory byte buffer at offsets the variant reads from. For hand-authored fixtures only — telemetry-captured fixtures use raw base64.</summary>
public sealed class AddonMemoryBuilder
{
    public const int BufferSize = 0x3000;
    private readonly byte[] memory = new byte[BufferSize];
    private readonly LayoutProfile profile;

    public AddonMemoryBuilder(LayoutProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        this.profile = profile;
    }

    public AddonMemoryBuilder WithScores(int self, int shimocha, int toimen, int kamicha)
    {
        WriteInt32(profile.Offsets.SelfScore, self);
        WriteInt32(profile.Offsets.ShimochaScore, shimocha);
        WriteInt32(profile.Offsets.ToimenScore, toimen);
        WriteInt32(profile.Offsets.KamichaScore, kamicha);
        return this;
    }

    public AddonMemoryBuilder WithDiscardCounts(int self, int shimocha, int toimen, int kamicha)
    {
        memory[profile.Offsets.SelfDiscardCountByte] = (byte)self;
        memory[profile.Offsets.ShimochaDiscardCountByte] = (byte)shimocha;
        memory[profile.Offsets.ToimenDiscardCountByte] = (byte)toimen;
        memory[profile.Offsets.KamichaDiscardCountByte] = (byte)kamicha;
        return this;
    }

    public AddonMemoryBuilder WithHand(string tileExpr)
    {
        var tiles = Tiles.Parse(tileExpr);
        int len = Math.Min(tiles.Length, profile.Limits.HandSize);
        for (int i = 0; i < len; i++)
            WriteInt32(profile.Offsets.HandArrayStart + i * 4, profile.TileTextureBase + tiles[i].Id);
        return this;
    }

    public AddonMemoryBuilder WithDoraIndicator(string tileExpr)
    {
        var tiles = Tiles.Parse(tileExpr);
        if (tiles.Length == 0)
            throw new ArgumentException("dora indicator tile expression must yield at least one tile", nameof(tileExpr));
        WriteInt32(profile.Offsets.DoraIndicator, profile.TileTextureBase + tiles[0].Id);
        return this;
    }

    public byte[] Build()
    {
        var copy = new byte[memory.Length];
        Buffer.BlockCopy(memory, 0, copy, 0, memory.Length);
        return copy;
    }

    private void WriteInt32(int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(memory.AsSpan(offset, 4), value);
}
