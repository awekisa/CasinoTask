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

    public T Read<T>(Func<IWalletSession, T> action)
    {
        using var db = new WalletDbContext(connectionString, timeoutSeconds);
        return action(new EfWalletSession(db));
    }

    public T Write<T>(Func<IWalletSession, T> action)
    {
        return PostgresPersistence.Write(() => new WalletDbContext(connectionString, timeoutSeconds),
            db => action(new EfWalletSession(db)), maxTransactionRetries);
    }
}
