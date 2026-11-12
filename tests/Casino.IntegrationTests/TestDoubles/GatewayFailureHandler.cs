using System.Net;

namespace Casino.IntegrationTests.TestDoubles;

public sealed class GatewayFailureHandler(HttpStatusCode status) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(status));
    }
}
