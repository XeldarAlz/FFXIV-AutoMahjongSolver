using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Composition;

public sealed class DalamudConfigService : IConfigService<Configuration>
{
    private readonly Action<Configuration> save;
    private readonly object updateLock = new();

    public DalamudConfigService(Action<Configuration> save, Configuration initial)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(initial);
        this.save = save;
        Current = initial;
    }

    public Configuration Current { get; private set; }

    public event Action<Configuration>? Changed;

    public void Update(Func<Configuration, Configuration> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        Configuration next;
        lock (updateLock)
        {
            next = mutate(Current)
                ?? throw new InvalidOperationException(
                    "Configuration mutator returned null.");

            // Persist before swapping — if save throws, Current stays at the last good value.
            save(next);
            Current = next;
        }

        Changed?.Invoke(next);
    }
}
