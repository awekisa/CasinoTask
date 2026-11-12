using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Persistence;
using Casino.IntegrationTests.Hosting;
using Casino.Wallet.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Casino.IntegrationTests.Http;

public sealed class LoadBalancerTests : IAsyncLifetime
{
    private readonly PostgresTestDatabases databases = new();
    private readonly List<RunningService> processes = [];
    private RunningService[] wallets = [];
    private RunningService[] games = [];
    private RunningService proxy = null!;
    private HttpClient wallet = null!;
    private HttpClient game = null!;
    private Uri[] addresses = [];

    public async Task InitializeAsync()
    {
        try
        {
            // Hold all six reservations simultaneously so the chosen ports are distinct.
            var listeners = Enumerable.Range(0, 6).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
            try
            {
                foreach (var listener in listeners) listener.Start();
                addresses = listeners.Select(listener => new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/")).ToArray();
            }
            finally { foreach (var listener in listeners) listener.Stop(); }
            wallets = [await Start("Wallet", addresses[0]), await Start("Wallet", addresses[1])];
            games = [await Start("Game", addresses[2]), await Start("Game", addresses[3])];
            proxy = await StartProxy();
            wallet = new HttpClient { BaseAddress = addresses[4], Timeout = TimeSpan.FromSeconds(15) };
            game = new HttpClient { BaseAddress = addresses[5], Timeout = TimeSpan.FromSeconds(15) };
        }
        catch { await DisposeAsync(); throw; }
    }

    private async Task<RunningService> Start(string component, Uri address, IReadOnlyDictionary<string, string>? configuration = null)
    {
        var service = await RunningService.Start(component, component == "Wallet" ? databases.Wallet : databases.Game,
            component == "Game" ? addresses[4] : null, address, configuration);
        processes.Add(service);
        return service;
    }

    private async Task<RunningService> StartProxy(bool wildcard = false, bool health = true, bool noDestinations = false)
    {
        var settings = new Dictionary<string, string> { ["urls"] = $"{addresses[4]};{addresses[5]}" };
        foreach (var (name, offset, listener) in new[] { ("Wallet", 0, 4), ("Game", 2, 5) })
        {
            for (var i = 0; i < 2; i++)
            {
                settings[$"ReverseProxy:Routes:{name}:Match:Hosts:{i}"] = $"{(wildcard ? "*" : "127.0.0.1")}:{addresses[listener].Port}";
                settings[$"ReverseProxy:Clusters:{name}:Destinations:{name}{i + 1}:Address"] = addresses[offset + i].ToString();
            }
            settings[$"ReverseProxy:Clusters:{name}:HealthCheck:Active:Enabled"] = health.ToString();
            // Only this verification test changes the default fallback to all unhealthy destinations.
            if (noDestinations) settings[$"ReverseProxy:Clusters:{name}:HealthCheck:AvailableDestinationsPolicy"] = "HealthyAndUnknown";
        }
        var service = await RunningService.Start("LoadBalancer", "", configuration: settings, expectedListeners: 2);
        processes.Add(service);
        return service;
    }

    [Fact]
    public async Task RoutesBothListenersToTheirOwnInstancesAndLogsDestinations()
    {
        Assert.Equal(2, proxy.Addresses.Count);
        await AssertDistribution(wallet, addresses.Take(2));
        await AssertDistribution(game, addresses.Skip(2).Take(2));
        using var wrongWallet = await wallet.PostAsJsonAsync("bets", new MoneyRequest(Guid.NewGuid(), Guid.NewGuid(), 1m));
        using var wrongGame = await game.GetAsync($"wallets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, wrongWallet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongGame.StatusCode);
        Assert.Contains(proxy.Logs, line => line.Contains("Proxying to", StringComparison.Ordinal));
        Assert.Contains(proxy.Logs, line => line.Contains(addresses[0].ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task WalletFailoverAndRejoin()
    {
        await wallets[0].DisposeAsync();
        await Eventually(async () => await AllAnsweredBy(wallet, addresses[1]), TimeSpan.FromSeconds(10));
        wallets[0] = await Start("Wallet", addresses[0]);
        await Eventually(async () => (await Identities(wallet)).Count == 2, TimeSpan.FromSeconds(10));
        await AssertDistribution(wallet, addresses.Take(2));
    }

    [Fact]
    public async Task DuplicateDepositsAndBetsAcrossProxyHaveOneFinancialEffect()
    {
        var player = await CreatePlayer();
        var deposit = new MoneyRequest(player, Guid.NewGuid(), 10m);
        var first = await Post<Operation>(wallet, "deposits", deposit);
        for (var i = 0; i < 4; i++) Assert.Equal(first, await Post<Operation>(wallet, "deposits", deposit));
        var copies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Post<Operation>(wallet, "deposits", deposit)));
        Assert.All(copies, result => Assert.Equal(first, result));
        using var depositConflict = await wallet.PostAsJsonAsync("deposits", deposit with { Amount = 11m });
        Assert.Equal(HttpStatusCode.Conflict, depositConflict.StatusCode);

        var bet = new MoneyRequest(player, Guid.NewGuid(), 2m);
        var rounds = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Post<Round>(game, "bets", bet)));
        Assert.Single(rounds.Select(round => round.Id).Distinct());
        Round? settled = null;
        await Eventually(async () =>
        {
            settled = await Post<Round>(game, "bets", bet);
            return settled.State == RoundState.Settled;
        });
        for (var i = 0; i < 4; i++) Assert.Equal(settled, await Post<Round>(game, "bets", bet));
        using var betConflict = await game.PostAsJsonAsync("bets", bet with { Amount = 3m });
        Assert.Equal(HttpStatusCode.Conflict, betConflict.StatusCode);
        using var walletDb = new WalletDbContext(databases.Wallet, 15);
        using var gameDb = new GameDbContext(databases.Game, 15);
        Assert.Equal(1, await walletDb.Operations.CountAsync());
        Assert.Equal(1, await walletDb.Reservations.CountAsync());
        Assert.Equal(1, await gameDb.Rounds.CountAsync());
        Assert.Equal(new Balance(8m + settled!.Payout!.Value, 0m), await wallet.GetFromJsonAsync<Balance>($"wallets/{player}"));
    }

    [Fact]
    public async Task LostSuccessfulResponseCanBeReplayedOnAnotherInstance()
    {
        var player = await CreatePlayer();
        using var handler = new LostResponseHandler();
        using var client = new HttpClient(handler) { BaseAddress = wallet.BaseAddress };
        var request = new MoneyRequest(player, Guid.NewGuid(), 12.34m);
        var result = await Post<Operation>(client, "deposits", request);
        Assert.Equal(new Balance(12.34m, 0), result.Balance);
        Assert.Equal(2, handler.Instances.Distinct().Count());
        using var walletDb = new WalletDbContext(databases.Wallet, 15);
        Assert.Equal(1, await walletDb.Operations.CountAsync());
        Assert.Equal(result.Balance, await wallet.GetFromJsonAsync<Balance>($"wallets/{player}"));
    }

    [Fact]
    public async Task ConcurrentBetsRecoverAfterGameStopsWithoutLosingMoney()
    {
        var player = await CreatePlayer();
        await Post<Operation>(wallet, "deposits", new MoneyRequest(player, Guid.NewGuid(), 5m));
        var requests = Enumerable.Range(0, 12).Select(_ => new MoneyRequest(player, Guid.NewGuid(), 2m)).ToArray();
        // Block reservations briefly so killing Game deterministically interrupts persisted rounds.
        await using var connection = new NpgsqlConnection(databases.Wallet);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE reservations IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await command.ExecuteNonQueryAsync();
        var submissions = requests.Select(request => SubmitDuringFailure(request)).ToArray();
        try
        {
            await Eventually(async () =>
            {
                using var db = new GameDbContext(databases.Game, 15);
                return await db.Rounds.CountAsync(round => round.State == RoundState.Pending) >= 2;
            });
            await games[0].DisposeAsync();
        }
        finally { await transaction.RollbackAsync(); }
        await Task.WhenAll(submissions);

        await Eventually(async () => await AllAnsweredBy(game, addresses[3]), TimeSpan.FromSeconds(10));
        // Reuse each saved request ID, including requests whose response or initial creation was lost.
        await Task.WhenAll(requests.Select(request => Post<Round>(game, "bets", request)));
        await Eventually(async () =>
        {
            var balance = (await wallet.GetFromJsonAsync<Balance>($"wallets/{player}"))!;
            Assert.True(balance.Total >= 0 && balance.Available >= 0 && balance.Held >= 0);
            using var db = new GameDbContext(databases.Game, 15);
            return await db.Rounds.CountAsync() == requests.Length &&
                !await db.Rounds.AnyAsync(round => round.State == RoundState.Pending || round.State == RoundState.Decided);
        });
        using var gameDb = new GameDbContext(databases.Game, 15);
        var rounds = await gameDb.Rounds.ToListAsync();
        var settled = rounds.Where(round => round.State == RoundState.Settled).ToArray();
        var expected = 5m - settled.Sum(round => round.Stake) + settled.Sum(round => round.Payout!.Value);
        Assert.Equal(new Balance(expected, 0), await wallet.GetFromJsonAsync<Balance>($"wallets/{player}"));
        using var walletDb = new WalletDbContext(databases.Wallet, 15);
        Assert.Equal(requests.Length, await walletDb.Reservations.CountAsync());
        Assert.Equal(settled.Length, await walletDb.Reservations.CountAsync(reservation =>
            reservation.Status == Casino.Wallet.Persistence.Enums.ReservationStatus.Settled));
        Assert.False(await walletDb.Reservations.AnyAsync(reservation =>
            reservation.Status == Casino.Wallet.Persistence.Enums.ReservationStatus.Held));
    }

    [Fact]
    public async Task WildcardHostWithPortWorksAndConnectionRefusalIs502()
    {
        await proxy.DisposeAsync();
        proxy = await StartProxy(wildcard: true, health: false);
        await AssertDistribution(wallet, addresses.Take(2));
        await AssertDistribution(game, addresses.Skip(2).Take(2));
        await wallets[0].DisposeAsync();
        await wallets[1].DisposeAsync();
        using var refused = await wallet.GetAsync("health");
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
    }

    [Fact]
    public async Task DefaultHealthPolicyFallsBackToUnhealthyButEmptyAvailableSetReturns503()
    {
        await wallets[0].DisposeAsync();
        await wallets[1].DisposeAsync();
        await Task.Delay(TimeSpan.FromSeconds(7));
        using (var fallback = await wallet.GetAsync("health"))
            Assert.Equal(HttpStatusCode.BadGateway, fallback.StatusCode);
        await proxy.DisposeAsync();
        proxy = await StartProxy(noDestinations: true);
        await Eventually(async () =>
        {
            using var response = await wallet.GetAsync("health");
            return response.StatusCode == HttpStatusCode.ServiceUnavailable;
        }, TimeSpan.FromSeconds(10));
    }

    private async Task SubmitDuringFailure(MoneyRequest request)
    {
        try
        {
            using var response = await game.PostAsJsonAsync("bets", request);
            Assert.True(response.IsSuccessStatusCode || (int)response.StatusCode is 502 or 503 or 504,
                $"Unexpected status: {response.StatusCode}");
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }

    private async Task<Guid> CreatePlayer()
    {
        var player = Guid.NewGuid();
        using var response = await wallet.PutAsync($"wallets/{player}", null);
        response.EnsureSuccessStatusCode();
        return player;
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static string Identity(Uri address) => $"127.0.0.1:{address.Port}";

    private static async Task<HashSet<string>> Identities(HttpClient client)
    {
        var identities = new HashSet<string>();
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync("health");
            if (!response.IsSuccessStatusCode) return [];
            identities.Add(response.Headers.GetValues("X-Instance").Single());
        }
        return identities;
    }

    private static async Task AssertDistribution(HttpClient client, IEnumerable<Uri> expected)
    {
        var identities = await Identities(client);
        Assert.Equal(expected.Select(Identity).Order(), identities.Order());
    }

    private static async Task<bool> AllAnsweredBy(HttpClient client, Uri expected)
    {
        var identities = await Identities(client);
        return identities.SetEquals([Identity(expected)]);
    }

    private static async Task Eventually(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(150);
        }
        Assert.Fail("Condition did not become true before the deadline.");
    }

    public async Task DisposeAsync()
    {
        wallet?.Dispose();
        game?.Dispose();
        foreach (var process in processes.AsEnumerable().Reverse()) await process.DisposeAsync();
        databases.Dispose();
    }
}
