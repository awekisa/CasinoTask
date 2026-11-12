using Npgsql;

namespace Casino.IntegrationTests.Hosting;

public sealed class PostgresTestDatabases : IDisposable
{
    public const string DefaultAdminConnection = "Host=localhost;Port=5432;Database=postgres;Username=casino_admin;Password=casino_local_admin";
    private readonly string adminConnection;
    private readonly List<string> created = [];
    public string Wallet { get; }
    public string Game { get; }

    public PostgresTestDatabases()
    {
        adminConnection = Environment.GetEnvironmentVariable("CASINO_TEST_POSTGRES") ?? DefaultAdminConnection;
        try
        {
            Wallet = Create("wallet");
            Game = Create("game");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private string Create(string service)
    {
        var name = $"casino_test_{service}_{Guid.NewGuid():N}";
        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        command.ExecuteNonQuery();
        created.Add(name);
        return new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = true, MaxPoolSize = 30 }.ConnectionString;
    }

    public void Dispose()
    {
        if (created.Count == 0) return;
        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();
        foreach (var name in created)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^casino_test_(wallet|game)_[a-f0-9]{32}$"))
                throw new InvalidOperationException("Refusing to drop a non-test database.");
            using var pool = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = true, MaxPoolSize = 30 }.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            command.ExecuteNonQuery();
        }
        created.Clear();
    }
}
