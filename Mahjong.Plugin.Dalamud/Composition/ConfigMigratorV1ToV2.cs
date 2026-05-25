using System;
using Mahjong.Plugin.Game;

namespace Mahjong.Plugin.Dalamud.Composition;

/// <summary>Mints a per-install InstallId once; never rotates — losing it makes the server treat the install as new.</summary>
internal sealed class ConfigMigratorV1ToV2 : IConfigMigrator<Configuration>
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public Configuration Migrate(Configuration input) =>
        input with
        {
            Version = ToVersion,
            InstallId = input.InstallId == Guid.Empty ? Guid.NewGuid() : input.InstallId,
        };
}
