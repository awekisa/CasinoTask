namespace Casino.Wallet.Persistence;

public interface IWalletDatabase
{
    T Read<T>(Func<IWalletSession, T> action);
    // The implementation must run reads, checks, changes and inserts atomically.
    T Write<T>(Func<IWalletSession, T> action);
}
