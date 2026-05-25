namespace Mahjong.Plugin.Game;

public interface IConfigMigrator<TConfig> where TConfig : class
{
    int FromVersion { get; }
    int ToVersion { get; }

    /// <summary>Must not mutate the input — return a new instance.</summary>
    TConfig Migrate(TConfig input);
}
