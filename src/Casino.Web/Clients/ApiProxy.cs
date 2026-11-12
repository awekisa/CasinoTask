using System.Net.Http.Headers;

namespace Casino.Web.Clients;

// Only forwards requests. Wallet and Game remain responsible for all business rules.
public sealed class ApiProxy(IHttpClientFactory clients)
{
    public async Task Forward(HttpContext context, string service, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
        if (HttpMethods.IsPost(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            using var client = clients.CreateClient(service);
            using var response = await client.SendAsync(request, context.RequestAborted);
            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                await Unavailable(context, service);
                return;
            }
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) &&
                                   !context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
        {
            await Unavailable(context, service);
        }
    }

    private static async Task Unavailable(HttpContext context, string service)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = $"{service} is unavailable. Start the service, then retry the saved request." });
    }
}
