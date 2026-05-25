using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.Logging;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed class MemoryDumpRecorder : IDisposable
{
    // v2 adds agent_addr + agent_b64; v1 entries are missing-field-tolerant.
    public const int SchemaVersion = 2;

    // The state-change cadence shows AtkValuesCount in three buckets (50/73/109); only 109 carries gameplay signal.
    internal const int MinAtkValuesForStateChangeDump = 100;

    private const int AddonDumpBytes = 0x300 + 0x1000;
    private const int RootDumpBytes = 0x400;
    private const int SeatPoolDumpBytes = 0x1000;
    private const int MaxAtkValues = 1024;
    private const long FileRolloverBytes = 1024 * 1024;

    // Opponent discard arrays live in AgentEmj, not the AtkUnitBase range — the addon's per-seat block only mutates the count byte.
    private const int AgentDumpBytes = 0x2000;
    private const int AgentEmjId = 5;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AddonEmjReader reader;
    private readonly SeatPoolRegistry seatPools;
    private readonly ErrorSink errors;
    private readonly string memdumpsDir;
    private readonly HashSet<string> seenHashes = new();
    private readonly object writerLock = new();
    private string? currentPath;
    private long currentBytes;
    private long sequence;
    private bool disposed;

    public string MemdumpsDir => memdumpsDir;

    public MemoryDumpRecorder(
        AddonEmjReader reader,
        SeatPoolRegistry seatPools,
        ErrorSink errors,
        string pluginConfigDirectory)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(seatPools);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDirectory);
        this.reader = reader;
        this.seatPools = seatPools;
        this.errors = errors;
        memdumpsDir = Path.Combine(pluginConfigDirectory, "memdumps");
        try
        { Directory.CreateDirectory(memdumpsDir); }
        catch { }
    }

    public void Dispose() => disposed = true;

    /// <summary>"state-change" is gated on AtkValuesCount; "input-pre" / "input-post" bypass the gate.</summary>
    public unsafe void Record(string reason)
    {
        if (disposed)
            return;

        try
        {
            var obs = reader.LastObservation;
            if (!obs.Present || obs.Address == 0)
                return;

            var unit = (AtkUnitBase*)obs.Address;

            bool isStateChange = reason == "state-change";
            if (isStateChange && unit->AtkValuesCount < MinAtkValuesForStateChangeDump)
                return;

            var entry = BuildEntry(reason, unit);

            // Dedup only state-change — input-pre/post need the bracket even on identical layouts.
            if (isStateChange)
            {
                if (seenHashes.Contains(entry.Hash))
                    return;
                seenHashes.Add(entry.Hash);
            }

            WriteEntry(entry);
        }
        catch (Exception ex)
        {
            errors.RecordException("MemoryDumpRecorder.Record", ex);
        }
    }

    private unsafe MemDumpEntry BuildEntry(string reason, AtkUnitBase* unit)
    {
        var addonBytes = SafeReadBytes((nint)unit, AddonDumpBytes);

        byte[]? rootBytes = null;
        nint rootAddr = 0;
        if (unit->RootNode != null)
        {
            rootAddr = (nint)unit->RootNode;
            rootBytes = SafeReadBytes(rootAddr, RootDumpBytes);
        }

        int atkCount = Math.Min((int)unit->AtkValuesCount, MaxAtkValues);
        byte[]? atkBytes = null;
        nint atkAddr = 0;
        if (unit->AtkValues != null && atkCount > 0)
        {
            atkAddr = (nint)unit->AtkValues;
            atkBytes = SafeReadBytes(atkAddr, atkCount * sizeof(AtkValue));
        }

        List<SeatPoolDump>? pools = null;
        foreach (var poolBase in seatPools.Bases)
        {
            var poolBytes = SafeReadBytes(poolBase, SeatPoolDumpBytes);
            if (poolBytes is null)
                continue;
            pools ??= new List<SeatPoolDump>(4);
            pools.Add(new SeatPoolDump(
                Address: poolBase.ToInt64(),
                BytesB64: Convert.ToBase64String(poolBytes)));
        }

        byte[]? agentBytes = null;
        nint agentAddr = 0;
        try
        {
            var module = AgentModule.Instance();
            if (module != null)
            {
                var agent = module->GetAgentByInternalId((AgentId)AgentEmjId);
                if (agent != null)
                {
                    agentAddr = (nint)agent;
                    agentBytes = SafeReadBytes(agentAddr, AgentDumpBytes);
                }
            }
        }
        catch (Exception ex)
        {
            // AgentModule pointer can be stale after a zone change; additive capture, don't fail the dump.
            errors.RecordException("MemoryDumpRecorder.AgentDump", ex);
        }

        var hash = ComputeHash(addonBytes, rootBytes, atkBytes, pools, agentBytes);

        var layout = reader.ActiveLayout;
        var seatOffsets = layout is null ? null : new AddonSeatOffsets(
            SelfCount: layout.Offsets.SelfDiscardCountByte,
            SelfScore: layout.Offsets.SelfScore,
            ShimochaCount: layout.Offsets.ShimochaDiscardCountByte,
            ShimochaScore: layout.Offsets.ShimochaScore,
            ToimenCount: layout.Offsets.ToimenDiscardCountByte,
            ToimenScore: layout.Offsets.ToimenScore,
            KamichaCount: layout.Offsets.KamichaDiscardCountByte,
            KamichaScore: layout.Offsets.KamichaScore,
            HandArrayStart: layout.Offsets.HandArrayStart);

        return new MemDumpEntry(
            T: NowIso(),
            Seq: Interlocked.Increment(ref sequence),
            V: SchemaVersion,
            Reason: reason ?? "(none)",
            AddonAddress: ((nint)unit).ToInt64(),
            AddonBytesB64: addonBytes is null ? null : Convert.ToBase64String(addonBytes),
            RootNodeAddress: rootAddr.ToInt64(),
            RootNodeBytesB64: rootBytes is null ? null : Convert.ToBase64String(rootBytes),
            AtkValuesAddress: atkAddr.ToInt64(),
            AtkValuesCount: atkCount,
            AtkValuesBytesB64: atkBytes is null ? null : Convert.ToBase64String(atkBytes),
            SeatPools: pools,
            AgentAddress: agentAddr.ToInt64(),
            AgentBytesB64: agentBytes is null ? null : Convert.ToBase64String(agentBytes),
            Variant: layout?.Name,
            AddonSeatOffsets: seatOffsets,
            Hash: hash);
    }

    /// <summary>Bad pointers raise AccessViolationException from native code; catch and return null so the rest of the snapshot still ships.</summary>
    private static unsafe byte[]? SafeReadBytes(nint address, int length)
    {
        if (address == 0 || length <= 0)
            return null;
        try
        {
            var buf = new byte[length];
            Marshal.Copy(address, buf, 0, length);
            return buf;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(
        byte[]? addonBytes, byte[]? rootBytes, byte[]? atkBytes, List<SeatPoolDump>? pools,
        byte[]? agentBytes)
    {
        using var sha = SHA256.Create();
        if (addonBytes is not null)
            sha.TransformBlock(addonBytes, 0, addonBytes.Length, null, 0);
        if (rootBytes is not null)
            sha.TransformBlock(rootBytes, 0, rootBytes.Length, null, 0);
        if (atkBytes is not null)
            sha.TransformBlock(atkBytes, 0, atkBytes.Length, null, 0);
        if (pools is not null)
        {
            foreach (var p in pools)
            {
                var b = Convert.FromBase64String(p.BytesB64);
                sha.TransformBlock(b, 0, b.Length, null, 0);
            }
        }
        if (agentBytes is not null)
            sha.TransformBlock(agentBytes, 0, agentBytes.Length, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).Substring(0, 16);
    }

    private void WriteEntry(MemDumpEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOpts);
        lock (writerLock)
        {
            if (currentPath is null || currentBytes >= FileRolloverBytes)
                RollFile();
            try
            {
                using var w = new StreamWriter(new FileStream(
                    currentPath!, FileMode.Append, FileAccess.Write, FileShare.Read));
                w.WriteLine(line);
                currentBytes += line.Length + 1;
            }
            catch (Exception ex)
            {
                errors.RecordException("MemoryDumpRecorder.WriteEntry", ex);
            }
        }
    }

    private void RollFile()
    {
        var fn = $"memdumps-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.ndjson";
        currentPath = Path.Combine(memdumpsDir, fn);
        currentBytes = 0;
    }

    private static string NowIso() =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private sealed record MemDumpEntry(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("addon_addr")] long AddonAddress,
        [property: JsonPropertyName("addon_b64")] string? AddonBytesB64,
        [property: JsonPropertyName("root_addr")] long RootNodeAddress,
        [property: JsonPropertyName("root_b64")] string? RootNodeBytesB64,
        [property: JsonPropertyName("atk_addr")] long AtkValuesAddress,
        [property: JsonPropertyName("atk_count")] int AtkValuesCount,
        [property: JsonPropertyName("atk_b64")] string? AtkValuesBytesB64,
        [property: JsonPropertyName("seat_pools")] List<SeatPoolDump>? SeatPools,
        [property: JsonPropertyName("agent_addr")] long AgentAddress,
        [property: JsonPropertyName("agent_b64")] string? AgentBytesB64,
        [property: JsonPropertyName("variant")] string? Variant,
        [property: JsonPropertyName("addon_seat_offsets")] AddonSeatOffsets? AddonSeatOffsets,
        [property: JsonPropertyName("hash")] string Hash);

    private sealed record SeatPoolDump(
        [property: JsonPropertyName("addr")] long Address,
        [property: JsonPropertyName("b64")] string BytesB64);

    private sealed record AddonSeatOffsets(
        [property: JsonPropertyName("self_count_byte")] int SelfCount,
        [property: JsonPropertyName("self_score")] int SelfScore,
        [property: JsonPropertyName("shimocha_count_byte")] int ShimochaCount,
        [property: JsonPropertyName("shimocha_score")] int ShimochaScore,
        [property: JsonPropertyName("toimen_count_byte")] int ToimenCount,
        [property: JsonPropertyName("toimen_score")] int ToimenScore,
        [property: JsonPropertyName("kamicha_count_byte")] int KamichaCount,
        [property: JsonPropertyName("kamicha_score")] int KamichaScore,
        [property: JsonPropertyName("hand_array_start")] int HandArrayStart);
}
