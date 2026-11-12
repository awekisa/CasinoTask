using Casino.Wallet.Configuration.Settings;
using Casino.Wallet.Persistence;
using Casino.Wallet.Persistence.Implementation;

namespace Casino.Wallet.Configuration.Extensions;

public static class WalletServiceCollectionExtensions
{
    public static IServiceCollection AddWalletPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetRequiredSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()!;
        databaseSettings.Validate();
        services.AddSingleton<IWalletDatabase>(new PostgresWalletDatabase(databaseSettings.ConnectionString,
            databaseSettings.TimeoutSeconds, databaseSettings.MaxTransactionRetries));
        services.AddSingleton<WalletStore>();
        return services;
    }
}
