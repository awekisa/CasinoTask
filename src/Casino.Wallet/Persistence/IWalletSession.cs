using Casino.Wallet.Persistence.Entities;

namespace Casino.Wallet.Persistence;

public interface IWalletSession
{
    WalletEntity? FindWallet(Guid playerId);

    OperationEntity? FindOperation(Guid playerId, Guid requestId);

    ReservationEntity? FindReservation(Guid roundId);

    void AddWallet(WalletEntity wallet);

    void AddOperation(OperationEntity operation);

    void AddReservation(ReservationEntity reservation);
}
