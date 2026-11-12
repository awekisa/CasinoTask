namespace Casino.Game.Configuration.Extensions;

public static class GameWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddGameServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddGamePersistence(builder.Configuration);
        builder.Services.AddWalletClient(builder.Configuration);
        builder.Services.AddRoundProcessing(builder.Configuration);
        builder.Services.AddControllers();
        return builder;
    }
}
