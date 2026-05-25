using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using Mahjong.Core;
using Mahjong.Plugin.Dalamud.Telemetry;
using Mahjong.Plugin.Game;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;

namespace Mahjong.Plugin.Dalamud.Hooks.Strategies;

/// <summary>Reports Seat=-1 — the asm site only sees R14 (per-seat pool struct), seat attribution requires snapshot mapping elsewhere.</summary>
public sealed class NativeAsmDiscardCapture : IDiscardCapture
{
    public const string Name = "native-asm";

    // Discard-handler inc+load+store sequence (20 bytes). Public so SigscanProbe can match the same pattern without instantiating this strategy.
    public const string DiscardSig =
        "41 FF 86 00 10 00 00 8B 85 90 00 00 00 41 89 86 04 10 00 00";

    // Ring buffer: +0x00 uint64 head (monotonic, slot = head & MASK), +0x10..+0x10+SLOTS*16 slots of (uint64 R14, int32 tileId, int32 _pad).
    private const int RingSlots = 64;
    private const int RingMask = 63;
    private const int SlotSize = 16;
    private const int RingDataOffset = 16;
    private const int BufferSize = RingDataOffset + RingSlots * SlotSize;

    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly SeatPoolRegistry? seatPools;
    private readonly ISigprobeLog sigprobes;
    private nint buffer;
    private IAsmHook? asmHook;
    private bool disposed;
    private ulong lastReadHead;
    private ulong totalCaptured;
    private int lastTileId = -1;

    public HookHealth Health { get; private set; } = HookHealth.Offline;
    public string StrategyName => Name;
    public ulong TotalCaptured => totalCaptured;
    public int LastTileId => lastTileId;
    public event Action<DiscardEvent>? DiscardObserved;

    public unsafe ulong NativeHitCount =>
        buffer == 0 ? 0 : *(ulong*)buffer;

    public NativeAsmDiscardCapture(
        IPluginLog log,
        IFramework framework,
        ISigScanner sigScanner,
        SeatPoolRegistry? seatPools = null,
        ISigprobeLog? sigprobes = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(sigScanner);
        this.log = log;
        this.framework = framework;
        this.seatPools = seatPools;
        this.sigprobes = sigprobes ?? NullSigprobeLog.Instance;

        AllocateBuffer();
        if (!TryActivateHook(sigScanner))
            return;

        Health = HookHealth.Active;
        framework.Update += OnFrameworkUpdate;
    }

    private void AllocateBuffer()
    {
        buffer = Marshal.AllocHGlobal(BufferSize);
        unsafe
        {
            var p = (byte*)buffer;
            for (int i = 0; i < BufferSize; i++)
                p[i] = 0;
        }
    }

    private bool TryActivateHook(ISigScanner sigScanner)
    {
        nint matchAddress;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            matchAddress = sigScanner.ScanText(DiscardSig);
            sw.Stop();
            sigprobes.Record(
                sigName: "doman.discard-handler",
                pattern: DiscardSig,
                matchAddress: matchAddress,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                success: true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            sigprobes.Record(
                sigName: "doman.discard-handler",
                pattern: DiscardSig,
                matchAddress: 0,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                success: false,
                errorMessage: ex.Message);
            log.Error($"[DiscardCapture/native-asm] sigscan failed: {ex.Message}");
            return false;
        }

        // Hook the `mov [r14+0x1004], eax` at sig+13; ExecuteFirst runs before the mov when EAX already holds tile_id.
        nint hookSite = matchAddress + 13;
        var asmBytes = BuildTrampoline((ulong)buffer);

        try
        {
            var hooks = Reloaded.Hooks.ReloadedHooks.Instance;
            asmHook = hooks
                .CreateAsmHook(asmBytes, (long)hookSite, AsmHookBehaviour.ExecuteFirst)
                .Activate();
            log.Info(
                $"[DiscardCapture/native-asm] activated at 0x{hookSite:X} " +
                $"(sig at 0x{matchAddress:X}, ring buffer at 0x{buffer:X}).");
            return true;
        }
        catch (Exception ex)
        {
            sigprobes.Record(
                sigName: "doman.discard-handler-asmhook",
                pattern: DiscardSig,
                matchAddress: hookSite,
                elapsedMs: 0,
                success: false,
                errorMessage: ex.Message);
            log.Error($"[DiscardCapture/native-asm] failed to activate: {ex}");
            asmHook = null;
            return false;
        }
    }

    /// <summary>4 pushes = 32 bytes keeps RSP aligned; no managed transition.</summary>
    private static byte[] BuildTrampoline(ulong bufferAddress) =>
    [
        0x50,
        0x51,
        0x52,
        0x41, 0x50,

        0x48, 0xB9,
            (byte)(bufferAddress        & 0xFF),
            (byte)((bufferAddress >>  8) & 0xFF),
            (byte)((bufferAddress >> 16) & 0xFF),
            (byte)((bufferAddress >> 24) & 0xFF),
            (byte)((bufferAddress >> 32) & 0xFF),
            (byte)((bufferAddress >> 40) & 0xFF),
            (byte)((bufferAddress >> 48) & 0xFF),
            (byte)((bufferAddress >> 56) & 0xFF),

        0x48, 0x8B, 0x11,
        0x4C, 0x8D, 0x42, 0x01,
        0x4C, 0x89, 0x01,
        0x48, 0x83, 0xE2, RingMask,
        0x48, 0xC1, 0xE2, 0x04,
        0x48, 0x8D, 0x4C, 0x11, RingDataOffset,

        0x4C, 0x89, 0x31,
        0x89, 0x41, 0x08,

        0x41, 0x58,
        0x5A,
        0x59,
        0x58,
    ];

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (buffer == 0 || disposed)
            return;
        var drained = DrainRingBuffer();
        if (drained.Count == 0)
            return;
        var now = DateTime.UtcNow;
        foreach (var (poolBase, tileId, seq) in drained)
        {
            totalCaptured++;
            lastTileId = tileId;
            // Register pool address even on torn-read tile_id — the address itself is valid signal.
            seatPools?.Observe(poolBase);
            if (tileId < 0 || tileId >= Tile.Count34)
                continue;
            DiscardObserved?.Invoke(new DiscardEvent(
                Seat: -1,
                Tile: Tile.FromId(tileId),
                ObservedAtUtc: now,
                SequenceNumber: seq));
        }
    }

    private unsafe List<(nint PoolBase, int TileId, ulong Seq)> DrainRingBuffer()
    {
        ulong head = *(ulong*)buffer;
        if (head == lastReadHead)
            return new(0);

        ulong startSeq = lastReadHead;
        ulong endSeq = head;
        if (endSeq - startSeq > (ulong)RingSlots)
            startSeq = endSeq - (ulong)RingSlots;

        var result = new List<(nint, int, ulong)>((int)(endSeq - startSeq));
        for (ulong seq = startSeq; seq < endSeq; seq++)
        {
            ulong slotIdx = seq & (ulong)RingMask;
            byte* slot = (byte*)(buffer + RingDataOffset + (long)slotIdx * SlotSize);
            nint poolBase = (nint)(*(ulong*)slot);
            int tileId = *(int*)(slot + 8);
            result.Add((poolBase, tileId, seq));
        }
        lastReadHead = head;
        return result;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        if (Health == HookHealth.Active)
            framework.Update -= OnFrameworkUpdate;

        try
        { asmHook?.Disable(); }
        catch { }
        asmHook = null;

        if (buffer != 0)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = 0;
        }

        Health = HookHealth.Offline;
    }
}
