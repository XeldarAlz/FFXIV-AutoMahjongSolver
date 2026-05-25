using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed class SeatPoolRegistry
{
    private readonly ConcurrentDictionary<nint, byte> bases = new();

    public IReadOnlyCollection<nint> Bases => (IReadOnlyCollection<nint>)bases.Keys;

    public void Observe(nint poolBase)
    {
        if (poolBase == 0 || poolBase == -1)
            return;
        bases.TryAdd(poolBase, 0);
    }
}
