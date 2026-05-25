using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mahjong.Plugin.Dalamud.Adapters;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Game;
using Mahjong.Policy.Abstractions.Random;
using Mahjong.Policy.Abstractions.Weights;
using Mahjong.Policy.Efficiency;
using Mahjong.Policy.Opponents;
using Mahjong.Policy.Placement;
using Mahjong.Rules;
using Mahjong.Rules.Rulesets;
using Mahjong.Rules.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace Mahjong.Plugin.Dalamud.Composition;

public static class PluginServices
{
    public static ServiceProvider Build(
        DalamudServices dalamud,
        Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(dalamud);
        ArgumentNullException.ThrowIfNull(configuration);

        var services = new ServiceCollection();

        RegisterDalamudAdapters(services, dalamud);
        RegisterConfiguration(services, dalamud.PluginInterface, configuration);
        RegisterRules(services);
        RegisterWeights(services);
        RegisterRandomness(services);
        RegisterPolicies(services);

        return services.BuildServiceProvider(validateScopes: false);
    }

    private static void RegisterConfiguration(
        IServiceCollection services,
        IDalamudPluginInterface pluginInterface,
        Configuration configuration)
    {
        services.AddSingleton<IConfigService<Configuration>>(
            new DalamudConfigService(pluginInterface.SavePluginConfig, configuration));
        services.AddSingleton<IConfigMigrator<Configuration>, ConfigMigratorV0ToV1>();
    }

    private static void RegisterDalamudAdapters(
        IServiceCollection services, DalamudServices dalamud)
    {
        services.AddSingleton(dalamud);
        services.AddSingleton(dalamud.Log);
        services.AddSingleton(dalamud.Framework);
        services.AddSingleton(dalamud.PluginInterface);
        services.AddSingleton(dalamud.CommandManager);
        services.AddSingleton(dalamud.ChatGui);
        services.AddSingleton(dalamud.ClientState);
        services.AddSingleton(dalamud.DataManager);
        services.AddSingleton(dalamud.Condition);
        services.AddSingleton(dalamud.GameGui);
        services.AddSingleton(dalamud.AddonLifecycle);
        services.AddSingleton(dalamud.SigScanner);
        services.AddSingleton(dalamud.GameInterop);

        services.AddSingleton<IEventLog, DalamudEventLog>();
        services.AddSingleton<IFrameworkScheduler, DalamudFrameworkScheduler>();
        services.AddSingleton<IGameClientAdapter, DalamudGameClientAdapter>();

        services.AddSingleton<MahjongAddon>();
    }

    private static void RegisterRules(IServiceCollection services)
    {
        services.AddSingleton<IRuleSet, DomanRuleSet>();
        services.AddSingleton<IScoringRule, StandardScoringRule>();
        services.AddSingleton<IDoraRule, StandardDoraRule>();
        services.AddSingleton<IFuRule, StandardFuRule>();
    }

    private static void RegisterWeights(IServiceCollection services)
    {
        services.AddSingleton<IWeightProvider, DefaultWeightProvider>();
    }

    private static void RegisterRandomness(IServiceCollection services)
    {
        services.AddSingleton<IRandomSource>(_ => new SeededRandomSource());
    }

    private static void RegisterPolicies(IServiceCollection services)
    {
        services.AddSingleton<IOpponentModel>(sp =>
            new OpponentModel(sp.GetRequiredService<IWeightProvider>().Current.Opponent));

        services.AddSingleton<IPlacementPolicy>(sp =>
            new PlacementAdjuster(sp.GetRequiredService<IWeightProvider>().Current.Placement));

        services.AddSingleton<IDiscardPolicy, HeuristicDiscardPolicy>();
        services.AddSingleton<ICallPolicy, HeuristicCallPolicy>();
        services.AddSingleton<IRiichiPolicy, HeuristicRiichiPolicy>();
        services.AddSingleton<IPushFoldPolicy, HeuristicPushFoldPolicy>();

        // Explicit factory pins the 6-arg ctor — EfficiencyPolicy also has a 1-arg ctor and MS.DI throws on ambiguous.
        services.AddSingleton<EfficiencyPolicy>(sp =>
            new EfficiencyPolicy(
                sp.GetRequiredService<IOpponentModel>(),
                sp.GetRequiredService<IDiscardPolicy>(),
                sp.GetRequiredService<ICallPolicy>(),
                sp.GetRequiredService<IRiichiPolicy>(),
                sp.GetRequiredService<IPushFoldPolicy>(),
                sp.GetRequiredService<IRuleSet>()));

        services.AddSingleton<IPolicy>(sp => sp.GetRequiredService<EfficiencyPolicy>());
    }
}
