using Casino.Web.Clients;
using Casino.Web.Configuration.Settings;

namespace Casino.Web.Configuration.Extensions;

public static class WebServiceCollectionExtensions
{
    public static IServiceCollection AddWalletAndGameApiProxy(this IServiceCollection services, IConfiguration configuration)
    {
        var walletSettings = configuration.GetRequiredSection("Wallet").Get<WalletApiSettings>()!;
        var gameSettings = configuration.GetRequiredSection("Game").Get<GameApiSettings>()!;
        walletSettings.Validate();
        gameSettings.Validate();
        services.AddSingleton(walletSettings);
        services.AddSingleton(gameSettings);
        services.AddHttpClient("Wallet", client =>
        {
            client.BaseAddress = new Uri(walletSettings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(walletSettings.TimeoutSeconds);
        });
        services.AddHttpClient("Game", client =>
        {
            client.BaseAddress = new Uri(gameSettings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(gameSettings.TimeoutSeconds);
        });
        services.AddTransient<ApiProxy>();
        return services;
    }
}
