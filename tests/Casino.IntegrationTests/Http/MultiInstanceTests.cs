using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.IntegrationTests.Hosting;
using Npgsql;
using Xunit;

namespace Casino.IntegrationTests.Http;

public sealed class MultiInstanceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabases databases = new();
    private readonly ConcurrentBag<RunningService> processes = [];
    private RunningService[] wallets = [];
    private RunningService[] games = [];

    public async Task InitializeAsync()
    {
        try
        {
            // Concurrent startup also exercises the database-wide schema initialization lock.
            wallets = await Task.WhenAll(Start("Wallet"), Start("Wallet"));
            games = await Task.WhenAll(Start("Game", wallets[0].Client.BaseAddress), Start("Game", wallets[1].Client.BaseAddress));
        }
        catch { await DisposeAsync(); throw; }
    }

    private async Task<RunningService> Start(string component, Uri? walletAddress = null, Uri? address = null)
    {
        var service = await RunningService.Start(component, component == "Wallet" ? databases.Wallet : databases.Game, walletAddress, address);
        processes.Add(service);
        return service;
    }

    private async Task<Guid> Fund(decimal amount = 10m)
    {
        var playerId = Guid.NewGuid();
        using var response = await wallets[0].Client.PutAsync($"wallets/{playerId}", null);
        response.EnsureSuccessStatusCode();
        await Post<Operation>(wallets[0].Client, "deposits", new MoneyRequest(playerId, Guid.NewGuid(), amount));
        return playerId;
    }

    [Fact]
    public async Task DuplicateRequestsAcrossReplicasHaveOneFinancialEffect()
    {
        var playerId = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            using var response = await wallets[index % 2].Client.PutAsync($"wallets/{playerId}", null);
            response.EnsureSuccessStatusCode();
        }));
        var deposit = new MoneyRequest(playerId, Guid.NewGuid(), 10.01m);
        var deposits = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            Post<Operation>(wallets[index % 2].Client, "deposits", deposit)));
        Assert.All(deposits, result => Assert.Equal(new Balance(10.01m, 0), result.Balance));
        using var conflict = await wallets[1].Client.PostAsJsonAsync("deposits", deposit with { Amount = 20m });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var bet = new MoneyRequest(playerId, Guid.NewGuid(), 5.01m);
        var rounds = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            Post<Round>(games[index % 2].Client, "bets", bet)));
        Assert.Single(rounds.Select(round => round.Id).Distinct());
        var settled = await WaitForSettlement(games[1].Client, rounds[0].Id);
        Assert.All(rounds.Where(round => round.State == RoundState.Settled), round => Assert.Equal(settled, round));
        Assert.Equal(settled, await Post<Round>(games[0].Client, "bets", bet));
        Assert.Equal(settled, await Post<Round>(games[1].Client, "bets", bet));
        using var betConflict = await games[1].Client.PostAsJsonAsync("bets", bet with { Amount = 6m });
        Assert.Equal(HttpStatusCode.Conflict, betConflict.StatusCode);
        var expected = new Balance(5m + settled.Payout!.Value, 0);
        foreach (var wallet in wallets)
            Assert.Equal(expected, await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
        Assert.Equal(1L, Count(databases.Wallet, "operations"));
        Assert.Equal(1L, Count(databases.Wallet, "reservations"));
        Assert.Equal(1L, Count(databases.Game, "rounds"));
    }

    [Fact]
    public async Task CompetingWithdrawalsAndHoldsAcrossReplicasCannotOverspend()
    {
        var playerId = await Fund();
        var ids = Enumerable.Range(0, 24).Select(_ => Guid.NewGuid()).ToArray();
        var results = await Task.WhenAll(ids.Select((id, index) => index % 2 == 0
            ? Post<Operation>(wallets[(index / 2) % 2].Client, "withdrawals", new MoneyRequest(playerId, id, 1m))
            : Post<Operation>(wallets[(index / 2) % 2].Client, "internal/reservations", new ReserveRequest(playerId, id, 1m))));
        Assert.Equal(10, results.Count(result => result.Status != OperationStatus.Rejected));
        var completed = results.Count(result => result.Status == OperationStatus.Completed);
        var held = results.Count(result => result.Status == OperationStatus.Held);
        var expected = new Balance(10m - completed, held);
        Assert.Equal(0m, expected.Available);
        foreach (var wallet in wallets)
            Assert.Equal(expected, await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
        // Settle every accepted hold twice, using both Wallet replicas.
        await Task.WhenAll(ids.SelectMany((id, index) => results[index].Status == OperationStatus.Held
            ? wallets.Select(wallet => Post<Operation>(wallet.Client, "internal/settlements", new SettleRequest(id, 0m)))
            : Enumerable.Empty<Task<Operation>>()));
        Assert.Equal(new Balance(0, 0), await wallets[1].Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
    }

    [Fact]
    public async Task AnotherGameInstanceRecoversWhenOriginatingInstanceStops()
    {
        var playerId = await Fund();
        var walletAddresses = wallets.Select(wallet => wallet.Client.BaseAddress!).ToArray();
        await Task.WhenAll(wallets.Select(wallet => wallet.DisposeAsync().AsTask()));
        var request = new MoneyRequest(playerId, Guid.NewGuid(), 5m);
        using var response = await games[0].Client.PostAsJsonAsync("bets", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var pending = (await response.Content.ReadFromJsonAsync<Round>())!;
        await games[0].DisposeAsync();
        wallets = await Task.WhenAll(Start("Wallet", address: walletAddresses[0]), Start("Wallet", address: walletAddresses[1]));
        var recovered = await WaitForSettlement(games[1].Client, pending.Id);
        games[0] = await Start("Game", walletAddresses[0]);
        Assert.Equal(recovered, await Post<Round>(games[0].Client, "bets", request));
        Assert.Equal(recovered, await Post<Round>(games[1].Client, "bets", request));
        var expected = new Balance(5m + recovered.Payout!.Value, 0);
        foreach (var wallet in wallets)
            Assert.Equal(expected, await wallet.Client.GetFromJsonAsync<Balance>($"wallets/{playerId}"));
        Assert.Equal(1L, Count(databases.Wallet, "reservations"));
        Assert.Equal(1L, Count(databases.Game, "rounds"));
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Round> WaitForSettlement(HttpClient client, Guid id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var round = (await client.GetFromJsonAsync<Round>($"rounds/{id}", deadline.Token))!;
            if (round.State == RoundState.Settled) return round;
            await Task.Delay(100, deadline.Token);
        }
    }

    private static long Count(string connectionString, string table)
    {
        if (table is not ("operations" or "reservations" or "rounds")) throw new ArgumentException("Unexpected table.", nameof(table));
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)command.ExecuteScalar()!;
    }

    public async Task DisposeAsync()
    {
        foreach (var service in processes) await service.DisposeAsync();
        databases.Dispose();
    }
}
