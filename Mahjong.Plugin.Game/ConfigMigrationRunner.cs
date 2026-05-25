namespace Mahjong.Plugin.Game;

/// <summary>Throws on gaps and non-progressing migrators — silent skips would corrupt state.</summary>
public static class ConfigMigrationRunner
{
    public static TConfig Run<TConfig>(
        TConfig config,
        int currentVersion,
        int targetVersion,
        IReadOnlyList<IConfigMigrator<TConfig>> migrators)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(migrators);

        if (currentVersion == targetVersion)
            return config;
        if (currentVersion > targetVersion)
            throw new InvalidOperationException(
                $"Persisted config version {currentVersion} is newer than the code's " +
                $"target version {targetVersion} — refusing to downgrade.");

        var current = config;
        int version = currentVersion;
        while (version < targetVersion)
        {
            var step = FindMigratorFrom(migrators, version)
                ?? throw new InvalidOperationException(
                    $"No migrator registered from version {version}. " +
                    $"Registered: [{string.Join(", ", migrators.Select(m => $"{m.FromVersion}→{m.ToVersion}"))}].");

            if (step.ToVersion <= step.FromVersion)
                throw new InvalidOperationException(
                    $"Migrator {step.GetType().Name} declares non-progressing " +
                    $"FromVersion={step.FromVersion} → ToVersion={step.ToVersion}.");

            current = step.Migrate(current)
                ?? throw new InvalidOperationException(
                    $"Migrator {step.GetType().Name} returned null.");
            version = step.ToVersion;
        }
        return current;
    }

    private static IConfigMigrator<TConfig>? FindMigratorFrom<TConfig>(
        IReadOnlyList<IConfigMigrator<TConfig>> migrators, int fromVersion)
        where TConfig : class
    {
        foreach (var m in migrators)
        {
            if (m.FromVersion == fromVersion)
                return m;
        }
        return null;
    }
}
