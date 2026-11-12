
namespace Casino.Web.Configuration.Settings;

public class EndpointSettings
{
    public const string WalletSectionName = "Wallet";
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; }
    public virtual void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !BaseUrl.EndsWith('/') ||
            TimeoutSeconds <= 0 || TimeoutSeconds > int.MaxValue / 1000)
            throw new InvalidOperationException("Endpoint requires an absolute HTTP(S) BaseUrl ending in / and a valid positive TimeoutSeconds.");
    }
    public static void ValidatePath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim() ||
            path.StartsWith('/') || path.Contains('\\') || path.Contains('?') || path.Contains('#') ||
            !Uri.TryCreate(path, UriKind.Relative, out _) || path.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidOperationException($"{name} requires a relative endpoint path without a leading slash, query, or fragment.");
    }
}

