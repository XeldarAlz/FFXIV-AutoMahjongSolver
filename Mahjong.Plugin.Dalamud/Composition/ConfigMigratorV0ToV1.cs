using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Composition;

internal sealed class ConfigMigratorV0ToV1 : IConfigMigrator<Configuration>
{
    public int FromVersion => 0;
    public int ToVersion => 1;

    public Configuration Migrate(Configuration input) =>
        input with { Version = ToVersion };
}
