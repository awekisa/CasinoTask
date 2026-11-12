namespace Casino.Wallet.Persistence.Implementation;

public sealed class PostgresWalletDatabase : IWalletDatabase
{
    private readonly string connectionString;
    private readonly int timeoutSeconds;
    private readonly int maxTransactionRetries;

    public PostgresWalletDatabase(string connectionString, int timeoutSeconds, int maxTransactionRetries = 20)
    {
        this.connectionString = connectionString;
        this.timeoutSeconds = timeoutSeconds;
        this.maxTransactionRetries = maxTransactionRetries;
        using var db = new WalletDbContext(connectionString, timeoutSeconds);
        PostgresPersistence.Initialize(db);
    }

    public async Task<T> Read<T>(Func<IWalletSession, Task<T>> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = new WalletDbContext(connectionString, timeoutSeconds);
        return await action(new EfWalletSession(db));
    }

    public Task<T> Write<T>(Func<IWalletSession, Task<T>> action, CancellationToken ct = default)
    {
        return PostgresPersistence.Write(() => new WalletDbContext(connectionString, timeoutSeconds),
            db => action(new EfWalletSession(db)), maxTransactionRetries, ct);
    }
}
