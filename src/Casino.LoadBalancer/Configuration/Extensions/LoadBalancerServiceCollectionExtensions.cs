namespace Casino.LoadBalancer.Configuration.Extensions;

public static class LoadBalancerServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
        return services;
    }
}
