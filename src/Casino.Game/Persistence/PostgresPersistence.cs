using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Casino.Game.Persistence;

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

    public static async Task<T> Write<TContext, T>(Func<TContext> createContext, Func<TContext, Task<T>> action, int maxRetries, CancellationToken ct)
        where TContext : DbContext
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await using var context = createContext();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action(context);
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException && attempt < maxRetries && IsRetryable(ex))
            {
                // Disposal rolls back an active transaction; a failed COMMIT may already have aborted it.
                // Retry the entire read/check/write with fresh tracking and a new snapshot.
                await Task.Delay(Random.Shared.Next(5, Math.Min(200, 10 * (attempt + 1)) + 1), ct);
            }
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        var postgres = exception.GetBaseException() as PostgresException;
        return postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected ||
            // Concurrent first inserts can race before an idempotency row exists.
            (postgres?.SqlState == PostgresErrorCodes.UniqueViolation &&
             postgres.ConstraintName is "PK_rounds" or "IX_rounds_player_request");
    }
}
