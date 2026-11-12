using Casino.Wallet.Persistence.Enums;
using Casino.IntegrationTests.Hosting;
using Casino.Game.Persistence.Implementation;
using Casino.Contracts.Wallet.Requests;
using Casino.Game.Persistence;
using Casino.Wallet.Persistence;
using Npgsql;
using Xunit;

namespace Casino.IntegrationTests.Architecture;

public sealed class ComponentIsolationTests
{
    [Theory]
    [InlineData(typeof(WalletStore))]
    [InlineData(typeof(GameStore))]
    public void ServicesShareOnlyContracts(Type serviceType)
    {
        var applicationReferences = serviceType.Assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name!.StartsWith("Casino."))
            .Select(reference => reference.Name)
            .ToArray();
        Assert.Equal(new[] { "Casino.Contracts" }, applicationReferences);
    }

    [Fact]
    public void ContractsHaveNoApplicationOrDatabaseDependencies()
    {
        var references = typeof(MoneyRequest).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("Casino.") ||
            reference.Name.StartsWith("Microsoft.EntityFrameworkCore") || reference.Name.StartsWith("Npgsql"));
    }

    [Fact]
    public async Task EachDatabaseContainsOnlyItsOwnServiceTables()
    {
        using var databases = new PostgresTestDatabases();
        var walletPath = databases.Wallet;
        var gamePath = databases.Game;
        {
            var wallet = new WalletStore(walletPath, 15);
            var game = new GameStore(gamePath, 15);
            var playerId = Guid.NewGuid();
            await wallet.Create(playerId);
            await wallet.Change(new(playerId, Guid.NewGuid(), 10m), MoneyOperationKind.Deposit);
            await game.GetOrCreate(new(playerId, Guid.NewGuid(), 2m));
            Assert.Equal(10m, (await wallet.Get(playerId)).Available);
            Assert.Equal(new[] { "operations", "reservations", "wallets" }, ReadTables(walletPath));
            Assert.Equal(new[] { "rounds" }, ReadTables(gamePath));
        }
    }

    private static string[] ReadTables(string path)
    {
        using var connection = new NpgsqlConnection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names.ToArray();
    }
}
