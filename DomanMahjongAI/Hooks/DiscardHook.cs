using DomanMahjongAI.Engine;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DomanMahjongAI.Hooks;

/// <summary>
/// Captures every Doman mahjong discard at the moment the game writes it to
/// its internal pool struct. We hook a single mid-function instruction inside
/// the discard handler — verified empirically via Cheat Engine on 2026-04-27:
///
///   ffxiv_dx11.exe + 0x1A20A36..0x1A20A49:
///     41 FF 86 00 10 00 00     inc [r14+0x1000]    ; pool's discard count++
///     8B 85 90 00 00 00         mov eax,[rbp+0x90]   ; load tile_id
///     41 89 86 04 10 00 00     mov [r14+0x1004],eax ; pool's latest discarded tile_id = eax
///
/// At hook time, <c>R14</c> = the pool struct base (one per seat) and <c>EAX</c>
/// = the tile_id of the discard. The 20-byte signature is unique in the binary.
///
/// The asm trampoline writes <c>(R14, EAX)</c> tuples into a 64-slot unmanaged
/// ring buffer. We avoid calling back into managed code from the hook — that
/// path failed silently in testing, almost certainly due to mid-function stack
/// alignment. The ring buffer is dirt-simple and dirt-safe: 7 register pushes,
/// no calls, no transitions. C# polls the buffer on framework update and
/// dedupes events per pool (the function fires multiple times per discard for
/// mirror/state-update reasons; only the latest per pool matters).
/// </summary>
public sealed class DiscardHook : IDisposable
{
    // Distinctive 20 bytes spanning the inc + load + store sequence inside the
    // discard handler. inc is 7 bytes, the [rbp+0x90] load is 6, the [r14+0x1004]
    // store is 7 — totaling 20 bytes that the AOB scanner will match on.
    private const string DiscardSig =
        "41 FF 86 00 10 00 00 8B 85 90 00 00 00 41 89 86 04 10 00 00";

    // Ring buffer layout (in unmanaged memory):
    //   +0x00..+0x07   uint64 head_index (monotonic counter, slot = head & MASK)
    //   +0x08..+0x0F   reserved
    //   +0x10..+0x10+(SLOTS*16)  slot[i] = (uint64 R14, int32 tileId, int32 _pad)
    private const int RingSlots = 64;        // power of two
    private const int RingMask  = 63;
    private const int SlotSize  = 16;
    private const int RingDataOffset = 16;
    private const int BufferSize = RingDataOffset + RingSlots * SlotSize;

    public readonly record struct DiscardEvent(
        nint PoolBase, int TileId, ulong SequenceNumber);

    private nint buffer;
    private IAsmHook? asmHook;
    private bool disposed;
    private ulong lastReadHead;
    private readonly string logPath;

    public bool Active => asmHook is not null;

    /// <summary>Total number of times the asm trampoline has fired.
    /// Read from the unmanaged head counter.</summary>
    public unsafe ulong DiagHitCount =>
        buffer == 0 ? 0 : *(ulong*)buffer;

    /// <summary>Tile_id of the most recent slot written. Diagnostic only —
    /// for actual data flow, drain the queue via <see cref="DrainNew"/>.</summary>
    public unsafe int DiagLastTileId
    {
        get
        {
            if (buffer == 0) return -1;
            ulong head = *(ulong*)buffer;
            if (head == 0) return -1;
            ulong slotIdx = (head - 1) & (ulong)RingMask;
            byte* slot = (byte*)(buffer + RingDataOffset + (long)slotIdx * SlotSize);
            return *(int*)(slot + 8);
        }
    }

    public DiscardHook()
    {
        var dir = Plugin.PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        logPath = Path.Combine(dir, "emj-discards.log");

        buffer = Marshal.AllocHGlobal(BufferSize);
        unsafe
        {
            var p = (byte*)buffer;
            for (int i = 0; i < BufferSize; i++) p[i] = 0;
        }

        nint matchAddress;
        try
        {
            matchAddress = Plugin.SigScanner.ScanText(DiscardSig);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"DiscardHook: sigscan failed: {ex.Message}");
            return;
        }

        // Hook AT the `mov [r14+0x1004], eax` instruction, offset +13 from sig
        // start. ExecuteFirst means our code runs BEFORE the original mov; EAX
        // already holds tile_id at this point (loaded by the preceding mov
        // eax,[rbp+0x90]) so we can safely read it.
        nint hookSite = matchAddress + 13;

        // Trampoline that writes (R14, EAX) to the next ring buffer slot.
        // Stack: 4 pushes (rax, rcx, rdx, r8) = 32 bytes, keeps RSP aligned.
        // No call, no managed transition — pure native increments and stores.
        //
        //   push rax
        //   push rcx
        //   push rdx
        //   push r8
        //
        //   mov  rcx, buffer_base
        //   mov  rdx, [rcx]               ; rdx = head
        //   lea  r8,  [rdx+1]
        //   mov  [rcx], r8                ; head++
        //   and  rdx, RingMask
        //   shl  rdx, 4                   ; * SlotSize (16)
        //   lea  rcx, [rcx + rdx + RingDataOffset]   ; slot ptr
        //   mov  [rcx], r14
        //   mov  [rcx+8], eax
        //
        //   pop  r8
        //   pop  rdx
        //   pop  rcx
        //   pop  rax
        var buf = (ulong)buffer;
        byte[] asmBytes =
        [
            0x50,                                        // push rax
            0x51,                                        // push rcx
            0x52,                                        // push rdx
            0x41, 0x50,                                  // push r8

            0x48, 0xB9,                                  // mov rcx, imm64
                (byte)(buf        & 0xFF),
                (byte)((buf >>  8) & 0xFF),
                (byte)((buf >> 16) & 0xFF),
                (byte)((buf >> 24) & 0xFF),
                (byte)((buf >> 32) & 0xFF),
                (byte)((buf >> 40) & 0xFF),
                (byte)((buf >> 48) & 0xFF),
                (byte)((buf >> 56) & 0xFF),

            0x48, 0x8B, 0x11,                            // mov rdx, [rcx]
            0x4C, 0x8D, 0x42, 0x01,                      // lea r8, [rdx+1]
            0x4C, 0x89, 0x01,                            // mov [rcx], r8
            0x48, 0x83, 0xE2, RingMask,                  // and rdx, 0x3F
            0x48, 0xC1, 0xE2, 0x04,                      // shl rdx, 4
            // lea rcx, [rcx + rdx + 0x10]
            //   REX.W=48, opcode 8D, ModRM = 4C (mod=01, reg=001=rcx, rm=100=SIB),
            //   SIB = 11 (scale=00, index=010=rdx, base=001=rcx), disp8=0x10
            0x48, 0x8D, 0x4C, 0x11, RingDataOffset,

            0x4C, 0x89, 0x31,                            // mov [rcx], r14
            0x89, 0x41, 0x08,                            // mov [rcx+8], eax

            0x41, 0x58,                                  // pop r8
            0x5A,                                        // pop rdx
            0x59,                                        // pop rcx
            0x58,                                        // pop rax
        ];

        var hooks = Reloaded.Hooks.ReloadedHooks.Instance;
        try
        {
            asmHook = hooks.CreateAsmHook(asmBytes, (long)hookSite, AsmHookBehaviour.ExecuteFirst)
                .Activate();
            Plugin.Log.Info(
                $"DiscardHook: ring-buffer asm hook activated at 0x{hookSite:X} " +
                $"(sig matched at 0x{matchAddress:X}, buffer at 0x{buffer:X}). " +
                $"Logging drained events to {logPath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"DiscardHook: failed to activate asm hook: {ex}");
            asmHook = null;
        }
    }

    /// <summary>
    /// Drain new events from the ring buffer. Call from the framework thread.
    /// Returns events in arrival order. If we missed events (head advanced by
    /// more than RingSlots since last call), only the most recent RingSlots
    /// events are recoverable.
    /// </summary>
    public unsafe IReadOnlyList<DiscardEvent> DrainNew()
    {
        if (buffer == 0) return Array.Empty<DiscardEvent>();
        ulong head = *(ulong*)buffer;
        if (head == lastReadHead) return Array.Empty<DiscardEvent>();

        // Read slots from lastReadHead up to head, bounded by RingSlots.
        ulong startSeq = lastReadHead;
        ulong endSeq = head;
        if (endSeq - startSeq > (ulong)RingSlots)
            startSeq = endSeq - (ulong)RingSlots;

        var result = new List<DiscardEvent>((int)(endSeq - startSeq));
        for (ulong seq = startSeq; seq < endSeq; seq++)
        {
            ulong slotIdx = seq & (ulong)RingMask;
            byte* slot = (byte*)(buffer + RingDataOffset + (long)slotIdx * SlotSize);
            nint poolBase = (nint)(*(ulong*)slot);
            int tileId = *(int*)(slot + 8);
            result.Add(new DiscardEvent(poolBase, tileId, seq));
        }

        lastReadHead = head;
        return result;
    }

    /// <summary>Append drained events to the diagnostic log file.</summary>
    public void LogDrained(IReadOnlyList<DiscardEvent> events)
    {
        if (events.Count == 0) return;
        try
        {
            using var w = new StreamWriter(new FileStream(
                logPath, FileMode.Append, FileAccess.Write, FileShare.Read));
            foreach (var e in events)
            {
                string tile = e.TileId >= 0 && e.TileId < Tile.Count34
                    ? Tile.FromId(e.TileId).ToString()
                    : "?";
                w.WriteLine(
                    $"{DateTime.UtcNow:o}  seq={e.SequenceNumber}  " +
                    $"pool=0x{e.PoolBase:X}  tile_id={e.TileId} ({tile})");
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { asmHook?.Disable(); } catch { }
        asmHook = null;
        if (buffer != 0)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = 0;
        }
    }
}
