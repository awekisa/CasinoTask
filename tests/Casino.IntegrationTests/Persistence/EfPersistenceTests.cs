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
    public void FailedSaveRollsBackMoneyAndIdempotencyRecord()
    {
        var store = new WalletStore(WalletConnectionString, 15);
        var player = Guid.NewGuid();
        store.Create(player);
        store.Change(new(player, Guid.NewGuid(), 10), "deposit");
        Execute(WalletConnectionString, "CREATE FUNCTION reject_wallet_update() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected storage failure'; END; $$; CREATE TRIGGER reject_wallet_update BEFORE UPDATE ON wallets FOR EACH ROW EXECUTE FUNCTION reject_wallet_update();");
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        Assert.Throws<DbUpdateException>(() => store.Change(request, "withdraw"));
        Assert.Equal(new Balance(10m, 0), store.Get(player));
        using (var db = new WalletDbContext(WalletConnectionString, 15))
            Assert.False(db.Operations.Any(o => o.Request == request.RequestId.ToString()));
        Execute(WalletConnectionString, "DROP TRIGGER reject_wallet_update ON wallets;");
        Assert.Equal(new Balance(5m, 0), store.Change(request, "withdraw").Balance);
        Assert.Equal(new Balance(5m, 0), store.Change(request, "withdraw").Balance);
    }

    [Fact]
    public async Task DecimalAmountsAndInterruptedRoundSurviveNewConnections()
    {
        var player = Guid.NewGuid();
        var request = new MoneyRequest(player, Guid.NewGuid(), 5.01m);
        var wallet = new WalletStore(WalletConnectionString, 15);
        wallet.Create(player);
        wallet.Change(new(player, Guid.NewGuid(), 10.02m), "deposit");
        var game = new GameStore(GameConnectionString, 15);
        var round = game.GetOrCreate(request);
        wallet.Reserve(new(player, round.Id, request.Amount));
        game.Decide(round.Id, 8.51m);
        var restartedWallet = new WalletStore(WalletConnectionString, 15);
        var engine = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(restartedWallet), new BrokenOutcome());
        Assert.Equal(8.51m, (await engine.Advance(round.Id)).Payout);
        Assert.Equal(new Balance(13.52m, 0), restartedWallet.Get(player));
        Assert.Equal(RoundState.Settled, (await engine.Place(request)).State);
    }

    [Fact]
    public void EfGeneratedSchemaEnforcesFinancialAndIdentityConstraints()
    {
        var store = new WalletStore(WalletConnectionString, 15);
        var player = Guid.NewGuid();
        store.Create(player);
        using (var db = new WalletDbContext(WalletConnectionString, 15))
        {
            var wallet = db.Wallets.Single();
            wallet.Held = 1;
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        var game = new GameStore(GameConnectionString, 15);
        var request = new MoneyRequest(player, Guid.NewGuid(), 1);
        game.GetOrCreate(request);
        using var context = new GameDbContext(GameConnectionString, 15);
        context.Rounds.Add(new RoundEntity { Id = Guid.NewGuid().ToString(), Player = player.ToString(), Request = request.RequestId.ToString(), Stake = 1m });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    public void Dispose()
    {
        databases.Dispose();
    }
}
