using Casino.Wallet.Persistence.Entities;

namespace Casino.Wallet.Persistence.Implementation;

public sealed class EfWalletSession(WalletDbContext db) : IWalletSession
{
    public WalletEntity? FindWallet(Guid playerId) => db.Wallets.SingleOrDefault(wallet => wallet.Player == playerId.ToString());
    public OperationEntity? FindOperation(Guid playerId, Guid requestId) =>
        db.Operations.SingleOrDefault(operation => operation.Player == playerId.ToString() && operation.Request == requestId.ToString());
    public ReservationEntity? FindReservation(Guid roundId) => db.Reservations.SingleOrDefault(reservation => reservation.Round == roundId.ToString());
    public void AddWallet(WalletEntity wallet) => db.Wallets.Add(wallet);
    public void AddOperation(OperationEntity operation) => db.Operations.Add(operation);
    public void AddReservation(ReservationEntity reservation) => db.Reservations.Add(reservation);
}
