using System.Collections.Concurrent;
using System.Diagnostics;

namespace Casino.IntegrationTests.Hosting;

public sealed class RunningService : IAsyncDisposable
{
    private readonly Process process;
    private int disposed;
    public HttpClient Client { get; }
    public IReadOnlyList<Uri> Addresses { get; }
    public IReadOnlyList<string> Logs => logs.ToArray();
    private readonly ConcurrentQueue<string> logs;

    private RunningService(Process process, IReadOnlyList<Uri> addresses, ConcurrentQueue<string> logs)
    {
        this.process = process;
        Addresses = addresses;
        this.logs = logs;
        Client = new HttpClient { BaseAddress = addresses[0], Timeout = TimeSpan.FromSeconds(15) };
    }

    public static async Task<RunningService> Start(string component, string connectionString, Uri? walletAddress = null, Uri? address = null, IReadOnlyDictionary<string, string>? configuration = null, int expectedListeners = 1)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Services", component, $"Casino.{component}.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var value in new[] { executable, "--urls", address?.ToString() ?? "http://127.0.0.1:0",
            "--Recovery:IntervalSeconds", "1" }) start.ArgumentList.Add(value);
        if (walletAddress is not null)
        {
            start.ArgumentList.Add("--Wallet:BaseUrl");
            start.ArgumentList.Add(walletAddress.ToString());
        }
        if (configuration is not null)
            foreach (var (key, value) in configuration)
            {
                start.ArgumentList.Add("--" + key);
                start.ArgumentList.Add(value);
            }
        start.Environment["Database__ConnectionString"] = connectionString;
        var listening = new TaskCompletionSource<IReadOnlyList<Uri>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<string>();
        var addresses = new List<Uri>();
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not { } line) return;
            logs.Enqueue(line);
            const string marker = "Now listening on: ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var uri))
            {
                addresses.Add(uri);
                if (addresses.Count == expectedListeners) listening.TrySetResult(addresses.ToArray());
            }
        };
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) logs.Enqueue(line); };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var uri = await listening.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return new RunningService(process, uri, logs);
        }
        catch (Exception ex)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
            throw new InvalidOperationException($"{component} failed to start: {string.Join(Environment.NewLine, logs)}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Client.Dispose();
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        process.Dispose();
    }
}
