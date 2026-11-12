using System.Net;
using System.Text.Json;
using Casino.IntegrationTests.TestDoubles;
using Casino.Web.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Casino.IntegrationTests.Http;

public sealed class ApiProxyTests
{
    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task EmptyGatewayFailuresBecomeJsonWithoutRetrying(int upstreamStatus)
    {
        using var handler = new GatewayFailureHandler((HttpStatusCode)upstreamStatus);
        var services = new ServiceCollection();
        services.AddHttpClient("Wallet", client => client.BaseAddress = new Uri("http://wallet.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var proxy = new ApiProxy(provider.GetRequiredService<IHttpClientFactory>());
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Response.Body = body;
        await proxy.Forward(context, "Wallet", "health");
        Assert.Equal(503, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Contains("Wallet is unavailable", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(1, handler.Calls);
    }
}
