using Npgsql;

namespace Casino.Wallet.Configuration.Settings;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; }
    public int MaxTransactionRetries { get; set; } = 20;

    public void Validate()
    {
        var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
            TimeoutSeconds <= 0 || MaxTransactionRetries < 0 || MaxTransactionRetries > 100)
            throw new InvalidOperationException("Database requires a PostgreSQL ConnectionString, positive TimeoutSeconds, and MaxTransactionRetries between 0 and 100.");
    }
}
