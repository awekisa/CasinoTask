namespace Casino.IntegrationTests.Hosting;

// Simulates a committed operation whose response was lost before reaching the caller.
// This retry exists only in the test client, never in the application or proxy.
public sealed class LostResponseHandler : DelegatingHandler
{
    private int discarded;
    public List<string> Instances { get; } = [];

    public LostResponseHandler() : base(new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
        var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode || Interlocked.Exchange(ref discarded, 1) != 0) return response;
        Instances.Add(response.Headers.GetValues("X-Instance").Single());
        response.Dispose();
        using var replay = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (body is not null)
        {
            replay.Content = new ByteArrayContent(body);
            foreach (var header in request.Content!.Headers) replay.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        var result = await base.SendAsync(replay, ct);
        Instances.Add(result.Headers.GetValues("X-Instance").Single());
        return result;
    }
}
