using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Casino.Wallet.Persistence;

public static class PostgresPersistence
{
    public static void Initialize(DbContext context)
    {
        // Serialize schema initialization across replicas. The database must already exist.
        context.Database.OpenConnection();
        context.Database.ExecuteSqlRaw("SELECT pg_advisory_lock(7139021)");
        try { context.Database.EnsureCreated(); }
        finally
        {
            context.Database.ExecuteSqlRaw("SELECT pg_advisory_unlock(7139021)");
            context.Database.CloseConnection();
        }
    }

    public static T Write<TContext, T>(Func<TContext> createContext, Func<TContext, T> action, int maxRetries)
        where TContext : DbContext
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var context = createContext();
                using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
                var result = action(context);
                context.SaveChanges();
                transaction.Commit();
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryable(ex))
            {
                // Disposal rolls back an active transaction; a failed COMMIT may already have aborted it.
                // Retry the entire read/check/write with fresh tracking and a new snapshot.
                Thread.Sleep(Random.Shared.Next(5, Math.Min(200, 10 * (attempt + 1)) + 1));
            }
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        var postgres = exception.GetBaseException() as PostgresException;
        return postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected ||
            // Concurrent first inserts can race before an idempotency row exists.
            (postgres?.SqlState == PostgresErrorCodes.UniqueViolation &&
             postgres.ConstraintName is "PK_wallets" or "PK_operations" or "PK_reservations");
    }
}
