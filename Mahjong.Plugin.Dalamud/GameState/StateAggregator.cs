using System;
using Dalamud.Plugin.Services;
using Mahjong.Engine;

namespace Mahjong.Plugin.Dalamud.GameState;

public sealed class StateAggregator : IDisposable
{
    private readonly AddonEmjReader reader;
    private readonly IFramework framework;
    private bool disposed;
    private long lastRebuildTicks;
    private const long MinTickIntervalTicks = 160_000;

    public StateSnapshot? Latest { get; private set; }

    public event Action<StateSnapshot>? Changed;

    public StateAggregator(AddonEmjReader reader, IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(framework);
        this.reader = reader;
        this.framework = framework;

        this.reader.ObservationChanged += OnObservationChanged;
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        framework.Update -= OnFrameworkUpdate;
        reader.ObservationChanged -= OnObservationChanged;
    }

    private void OnObservationChanged(AddonEmjObservation _) => Rebuild();

    private void OnFrameworkUpdate(IFramework _)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - lastRebuildTicks < MinTickIntervalTicks)
            return;
        lastRebuildTicks = now;
        Rebuild();
    }

    private void Rebuild()
    {
        var next = reader.TryBuildSnapshot();
        if (next is null)
            return;
        if (next.SchemaVersion != StateSnapshot.CurrentSchemaVersion)
            return;

        if (Latest is null || !SnapshotEqual(Latest, next))
        {
            Latest = next;
            Changed?.Invoke(next);
        }
    }

    private static bool SnapshotEqual(StateSnapshot a, StateSnapshot b)
    {
        if (ReferenceEquals(a, b))
            return true;
        return a == b;
    }
}
