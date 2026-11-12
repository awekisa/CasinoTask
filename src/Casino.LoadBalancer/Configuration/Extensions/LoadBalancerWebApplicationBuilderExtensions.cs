namespace Casino.LoadBalancer.Configuration.Extensions;

public static class LoadBalancerWebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddLoadBalancerServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddConfiguredReverseProxy(builder.Configuration);
        return builder;
    }
}
