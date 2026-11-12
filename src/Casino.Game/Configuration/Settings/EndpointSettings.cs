
namespace Casino.Game.Configuration.Settings;

public sealed class EndpointSettings
{
    public const string WalletSectionName = "Wallet";
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; }
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !BaseUrl.EndsWith('/') ||
            TimeoutSeconds <= 0 || TimeoutSeconds > int.MaxValue / 1000)
            throw new InvalidOperationException("Endpoint requires an absolute HTTP(S) BaseUrl ending in / and a valid positive TimeoutSeconds.");
    }
}
