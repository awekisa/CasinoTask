using System.Net;
using System.Net.Http.Json;
using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.IntegrationTests.Hosting;
using Xunit;

namespace Casino.IntegrationTests.Http;

// No mocks: independent service processes, the real HTTP client, recovery worker and PostgreSQL databases.
public sealed class WalletGameHttpTests : IAsyncLifetime
{
    private readonly PostgresTestDatabases databases = new();
    private RunningService? wallet;
    private RunningService? game;
    private string WalletConnectionString => databases.Wallet;
    private string GameConnectionString => databases.Game;

    public async Task InitializeAsync()
    {
        try
        {
            wallet = await RunningService.Start("Wallet", WalletConnectionString);
            game = await RunningService.Start("Game", GameConnectionString, wallet.Client.BaseAddress);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    [Fact]
    public async Task RealServicesPersistReplayAndRecoverAfterOutageAndRestart()
    {
        var playerId = Guid.NewGuid();
        using (var created = await wallet!.Client.PutAsync($"wallets/{playerId}", null))
            created.EnsureSuccessStatusCode();
        var deposit = new MoneyRequest(playerId, Guid.NewGuid(), 10.25m);
        var funded = await Post<Operation>(wallet!.Client, "deposits", deposit);
        Assert.Equal(new Balance(10.25m, 0), funded.Balance);
        Assert.Equal(funded, await Post<Operation>(wallet.Client, "deposits", deposit));

        var firstBet = new MoneyRequest(playerId, Guid.NewGuid(), 1.01m);
        var firstRound = await Post<Round>(game!.Client, "bets", firstBet);
        Assert.Equal(RoundState.Settled, firstRound.State);
        Assert.InRange(firstRound.Payout!.Value, 0m, 10.10m);
        var expected = 10.25m - 1.01m + firstRound.Payout.Value;
        Assert.Equal(new Balance(expected, 0), await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
        Assert.Equal(firstRound, await Post<Round>(game.Client, "bets", firstBet));

        var walletAddress = wallet.Client.BaseAddress!;
        await wallet.DisposeAsync();
        wallet = null;
        var interruptedBet = new MoneyRequest(playerId, Guid.NewGuid(), 1m);
        using var response = await game.Client.PostAsJsonAsync("bets", interruptedBet);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var pending = (await response.Content.ReadFromJsonAsync<Round>())!;
        Assert.Equal(RoundState.Pending, pending.State);

        await game.DisposeAsync();
        game = null;
        wallet = await RunningService.Start("Wallet", WalletConnectionString, address: walletAddress);
        game = await RunningService.Start("Game", GameConnectionString, walletAddress);
        Round? recovered = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        do
        {
            recovered = await game.Client.GetFromJsonAsync<Round>($"rounds/{pending.Id}", deadline.Token);
            if (recovered?.State == RoundState.Settled) break;
            await Task.Delay(100, deadline.Token);
        } while (true);

        Assert.Equal(recovered, await Post<Round>(game.Client, "bets", interruptedBet));
        Assert.Equal(new Balance(expected - 1m + recovered!.Payout!.Value, 0),
            await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
    }

    [Fact]
    public async Task ConcurrentHttpWithdrawalsCannotOverspend()
    {
        var playerId = Guid.NewGuid();
        using (var created = await wallet!.Client.PutAsync($"wallets/{playerId}", null))
            created.EnsureSuccessStatusCode();
        await Post<Operation>(wallet!.Client, "deposits", new MoneyRequest(playerId, Guid.NewGuid(), 10m));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Post<Operation>(wallet.Client, "withdrawals", new MoneyRequest(playerId, Guid.NewGuid(), 5m))));
        Assert.Equal(2, results.Count(result => result.Status == OperationStatus.Completed));
        Assert.Equal(6, results.Count(result => result.Status == OperationStatus.Rejected));
        Assert.Equal(new Balance(0, 0), await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
    }

    private static async Task<T> Post<T>(HttpClient client, string path, MoneyRequest request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task DisposeAsync()
    {
        if (game is not null)
        {
            await game.DisposeAsync();
            game = null;
        }
        if (wallet is not null)
        {
            await wallet.DisposeAsync();
            wallet = null;
        }
        databases.Dispose();
    }
}
