namespace Casino.Wallet.Persistence;

public interface IWalletDatabase
{
    Task<T> Read<T>(Func<IWalletSession, Task<T>> action, CancellationToken ct = default);
    // The implementation must run reads, checks, changes and inserts atomically.
    Task<T> Write<T>(Func<IWalletSession, Task<T>> action, CancellationToken ct = default);
}
