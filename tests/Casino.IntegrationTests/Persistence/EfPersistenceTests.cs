using Casino.Wallet.Persistence.Enums;
using Casino.IntegrationTests.Hosting;
using Casino.Game.Persistence.Implementation;
using Casino.Game.Rounds;
using Casino.Contracts.Game.Enums;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Persistence.Entities;
using Casino.Game.Persistence;
using Casino.IntegrationTests.TestDoubles;
using Casino.Wallet.Persistence;

using System.Text.Json;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Casino.IntegrationTests.Persistence;

public sealed class EfPersistenceTests : IDisposable
{
    private readonly PostgresTestDatabases databases = new();
    private string WalletConnectionString => databases.Wallet;
    private string GameConnectionString => databases.Game;

    private static void Execute(string path, string sql, params object[] args)
    {
        using var connection = new NpgsqlConnection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
            command.Parameters.AddWithValue("@p" + i, args[i]);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task FailedSaveRollsBackMoneyAndIdempotencyRecord()
    {
        var store = new WalletStore(WalletConnectionString, 15);
        var player = Guid.NewGuid();
        await store.Create(player);
        await store.Change(new(player, Guid.NewGuid(), 10), MoneyOperationKind.Deposit);
        Execute(WalletConnectionString, "CREATE FUNCTION reject_wallet_update() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected storage failure'; END; $$; CREATE TRIGGER reject_wallet_update BEFORE UPDATE ON wallets FOR EACH ROW EXECUTE FUNCTION reject_wallet_update();");
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Change(request, MoneyOperationKind.Withdraw));
        Assert.Equal(new Balance(10m, 0), await store.Get(player));
        using (var db = new WalletDbContext(WalletConnectionString, 15))
            Assert.False(db.Operations.Any(o => o.Request == request.RequestId));
        Execute(WalletConnectionString, "DROP TRIGGER reject_wallet_update ON wallets;");
        Assert.Equal(new Balance(5m, 0), (await store.Change(request, MoneyOperationKind.Withdraw)).Balance);
        Assert.Equal(new Balance(5m, 0), (await store.Change(request, MoneyOperationKind.Withdraw)).Balance);
    }

    [Fact]
    public async Task DecimalAmountsAndInterruptedRoundSurviveNewConnections()
    {
        var player = Guid.NewGuid();
        var request = new MoneyRequest(player, Guid.NewGuid(), 5.01m);
        var wallet = new WalletStore(WalletConnectionString, 15);
        await wallet.Create(player);
        await wallet.Change(new(player, Guid.NewGuid(), 10.02m), MoneyOperationKind.Deposit);
        var game = new GameStore(GameConnectionString, 15);
        var round = await game.GetOrCreate(request);
        await wallet.Reserve(new(player, round.Id, request.Amount));
        await game.Decide(round.Id, 8.51m);
        var restartedWallet = new WalletStore(WalletConnectionString, 15);
        var engine = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(restartedWallet), new BrokenOutcome());
        Assert.Equal(8.51m, (await engine.Advance(round.Id)).Payout);
        Assert.Equal(new Balance(13.52m, 0), await restartedWallet.Get(player));
        Assert.Equal(RoundState.Settled, (await engine.Place(request)).State);
    }

    [Fact]
    public async Task EfGeneratedSchemaEnforcesFinancialAndIdentityConstraints()
    {
        var store = new WalletStore(WalletConnectionString, 15);
        var player = Guid.NewGuid();
        await store.Create(player);
        using (var db = new WalletDbContext(WalletConnectionString, 15))
        {
            var wallet = db.Wallets.Single();
            wallet.Held = 1;
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        var game = new GameStore(GameConnectionString, 15);
        var request = new MoneyRequest(player, Guid.NewGuid(), 1);
        await game.GetOrCreate(request);
        using var context = new GameDbContext(GameConnectionString, 15);
        context.Rounds.Add(new RoundEntity { Id = Guid.NewGuid(), Player = player, Request = request.RequestId, Stake = 1m });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        foreach (var (connectionString, expectedColumns) in new[]
        {
            (WalletConnectionString, new[] { "operations.player:uuid", "operations.request:uuid", "reservations.player:uuid", "reservations.round:uuid", "wallets.player:uuid" }),
            (GameConnectionString, new[] { "rounds.id:uuid", "rounds.player:uuid", "rounds.request:uuid" })
        })
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT table_name || '.' || column_name || ':' || data_type
                FROM information_schema.columns
                WHERE table_schema = 'public'
                    AND table_name IN ('wallets', 'operations', 'reservations', 'rounds')
                    AND column_name IN ('id', 'player', 'request', 'round')
                ORDER BY table_name, column_name
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            Assert.Equal(expectedColumns, columns);
        }
    }

    public void Dispose()
    {
        databases.Dispose();
    }
}
