namespace Casino.Web.Configuration.Extensions;

public static class WebWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddWebProxyServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddWalletAndGameApiProxy(builder.Configuration);
        builder.Services.AddControllers();
        return builder;
    }
}
