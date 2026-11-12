using Microsoft.EntityFrameworkCore;
using Casino.Wallet.Persistence.Entities;

namespace Casino.Wallet.Persistence.Implementation;

public sealed class EfWalletSession(WalletDbContext db) : IWalletSession
{
    public Task<WalletEntity?> FindWallet(Guid playerId, CancellationToken ct = default) => db.Wallets.SingleOrDefaultAsync(wallet => wallet.Player == playerId.ToString(), ct);
    public Task<OperationEntity?> FindOperation(Guid playerId, Guid requestId, CancellationToken ct = default) =>
        db.Operations.SingleOrDefaultAsync(operation => operation.Player == playerId.ToString() && operation.Request == requestId.ToString(), ct);
    public Task<ReservationEntity?> FindReservation(Guid roundId, CancellationToken ct = default) => db.Reservations.SingleOrDefaultAsync(reservation => reservation.Round == roundId.ToString(), ct);
    public void AddWallet(WalletEntity wallet) => db.Wallets.Add(wallet);
    public void AddOperation(OperationEntity operation) => db.Operations.Add(operation);
    public void AddReservation(ReservationEntity reservation) => db.Reservations.Add(reservation);
}
