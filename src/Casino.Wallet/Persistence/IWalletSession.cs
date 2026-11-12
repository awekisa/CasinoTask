using Casino.Wallet.Persistence.Entities;

namespace Casino.Wallet.Persistence;

public interface IWalletSession
{
    Task<WalletEntity?> FindWallet(Guid playerId, CancellationToken ct = default);

    Task<OperationEntity?> FindOperation(Guid playerId, Guid requestId, CancellationToken ct = default);

    Task<ReservationEntity?> FindReservation(Guid roundId, CancellationToken ct = default);

    void AddWallet(WalletEntity wallet);

    void AddOperation(OperationEntity operation);

    void AddReservation(ReservationEntity reservation);
}
