using Casino.Game.Clients;
using Casino.Game.Clients.Implementation;
using Casino.Game.Configuration.Settings;
using Casino.Game.Persistence;
using Casino.Game.Persistence.Implementation;
using Casino.Game.Recovery;
using Casino.Game.Rounds;
using Casino.Game.Rounds.Implementation;

namespace Casino.Game.Configuration.Extensions;

public static class GameServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetRequiredSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()!;
        databaseSettings.Validate();
        services.AddSingleton<IGameStore>(new GameStore(databaseSettings.ConnectionString,
            databaseSettings.TimeoutSeconds, databaseSettings.MaxTransactionRetries));
        return services;
    }

    public static IServiceCollection AddWalletClient(this IServiceCollection services, IConfiguration configuration)
    {
        var walletSettings = configuration.GetRequiredSection(EndpointSettings.WalletSectionName).Get<EndpointSettings>()!;
        walletSettings.Validate();
        services.AddHttpClient<IWalletClient, HttpWalletClient>(client =>
        {
            client.BaseAddress = new Uri(walletSettings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(walletSettings.TimeoutSeconds);
        });
        return services;
    }

    public static IServiceCollection AddRoundProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var recoverySettings = configuration.GetRequiredSection(RecoverySettings.SectionName).Get<RecoverySettings>()!;
        recoverySettings.Validate();
        services.AddSingleton(recoverySettings);
        services.AddSingleton<IOutcome, RandomOutcome>();
        services.AddTransient<GameEngine>();
        services.AddHostedService<RoundRecoveryWorker>();
        return services;
    }
}
