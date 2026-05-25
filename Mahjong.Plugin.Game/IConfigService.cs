namespace Mahjong.Plugin.Game;

public interface IConfigService<TConfig> where TConfig : class
{
    TConfig Current { get; }

    /// <summary>Apply, persist, atomically swap, fire <see cref="Changed"/>.</summary>
    void Update(Func<TConfig, TConfig> mutate);

    event Action<TConfig>? Changed;
}
