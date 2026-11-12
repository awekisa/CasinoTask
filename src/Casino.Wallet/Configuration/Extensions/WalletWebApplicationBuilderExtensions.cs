namespace Casino.Wallet.Configuration.Extensions;

public static class WalletWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddWalletServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddWalletPersistence(builder.Configuration);
        builder.Services.AddControllers();
        return builder;
    }
}
