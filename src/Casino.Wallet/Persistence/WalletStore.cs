using System.Text.Json;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Errors.Exceptions;
using Casino.Wallet.Finance;
using Casino.Wallet.Persistence.Entities;
using Casino.Wallet.Persistence.Enums;

using Casino.Wallet.Persistence.Implementation;

namespace Casino.Wallet.Persistence;

public sealed class WalletStore
{
    private readonly IWalletDatabase database;

    public WalletStore(IWalletDatabase database)
    {
        this.database = database;
    }

    public WalletStore(string connectionString, int timeoutSeconds) : this(new PostgresWalletDatabase(connectionString, timeoutSeconds)) { }

    private Task<T> Write<T>(Func<IWalletSession, Task<T>> action, CancellationToken ct) => database.Write(action, ct);

    private static async Task<WalletEntity> Find(IWalletSession db, Guid player, CancellationToken ct) =>
        await db.FindWallet(player, ct) ?? throw new DomainException("Unknown player.", 404);
    private static Balance BalanceOf(WalletEntity wallet) => new(wallet.Total, wallet.Held);
    private static Operation Decode(string json) => JsonSerializer.Deserialize<Operation>(json)!;

    public Task<Balance> Create(Guid player, CancellationToken ct = default)
    {
        Money.Id(player);
        return Write(async db =>
        {
            var wallet = await db.FindWallet(player, ct);
            if (wallet is null)
            {
                wallet = new WalletEntity { Player = player.ToString() };
                db.AddWallet(wallet);
            }
            return BalanceOf(wallet);
        }, ct);
    }

    public Task<Balance> Get(Guid player, CancellationToken ct = default)
    {
        return database.Read(async db => BalanceOf(await Find(db, player, ct)), ct);
    }

    public Task<Operation> Change(MoneyRequest request, string kind, CancellationToken ct = default)
    {
        Money.Id(request.PlayerId);
        Money.Id(request.RequestId);
        if (kind is not ("deposit" or "withdraw")) throw new DomainException("Invalid operation.");
        var amount = Money.Validate(request.Amount);
        return Write(async db =>
        {
            var previous = await db.FindOperation(request.PlayerId, request.RequestId, ct);
            if (previous is not null)
            {
                if (previous.Kind != kind || previous.Amount != amount) throw new DomainException("Idempotency key reused with different data.", 409);
                return Decode(previous.Result);
            }
            var wallet = await Find(db, request.PlayerId, ct);
            var balance = BalanceOf(wallet);
            var error = kind == "withdraw" && balance.Available < amount ? "Insufficient available funds." :
                kind == "deposit" && balance.Total + amount + 9 * balance.Held > Money.MaxBalance ? "Balance limit exceeded." : null;
            if (error is null) wallet.Total += kind == "deposit" ? amount : -amount;
            var result = new Operation(error is null ? OperationStatus.Completed : OperationStatus.Rejected, BalanceOf(wallet), error);
            db.AddOperation(new OperationEntity
            {
                Player = request.PlayerId.ToString(),
                Request = request.RequestId.ToString(),
                Kind = kind,
                Amount = amount,
                Result = JsonSerializer.Serialize(result)
            });
            return result;
        }, ct);
    }

    public Task<Operation> Reserve(ReserveRequest request, CancellationToken ct = default)
    {
        Money.Id(request.RoundId);
        Money.Id(request.PlayerId);
        Money.Validate(request.Stake, bet: true);
        return Write(async db =>
        {
            var previous = await db.FindReservation(request.RoundId, ct);
            if (previous is not null)
            {
                if (previous.Player != request.PlayerId.ToString() || previous.Stake != request.Stake) throw new DomainException("Round reused with different data.", 409);
                return Decode(previous.Result);
            }
            var wallet = await db.FindWallet(request.PlayerId, ct);
            var balance = wallet is null ? new Balance(0, 0) : BalanceOf(wallet);
            string? error = wallet is null ? "Unknown player." : null;
            if (error is null && balance.Available < request.Stake) error = "Insufficient available funds.";
            if (error is null && balance.Total + 9 * (balance.Held + request.Stake) > Money.MaxBalance) error = "Balance limit exceeded.";
            if (error is null)
            {
                wallet!.Held += request.Stake;
                balance = BalanceOf(wallet);
            }
            var result = new Operation(error is null ? OperationStatus.Held : OperationStatus.Rejected, balance, error);
            db.AddReservation(new ReservationEntity
            {
                Round = request.RoundId.ToString(),
                Player = request.PlayerId.ToString(),
                Stake = request.Stake,
                Status = error is null ? ReservationStatus.Held : ReservationStatus.Rejected,
                Result = JsonSerializer.Serialize(result)
            });
            return result;
        }, ct);
    }

    public Task<Operation> Settle(SettleRequest request, CancellationToken ct = default) => Write(async db =>
    {
        var reservation = await db.FindReservation(request.RoundId, ct) ?? throw new DomainException("Unknown reservation.", 404);
        if (reservation.Status == ReservationStatus.Rejected) throw new DomainException("Cannot settle rejected reservation.", 409);
        if (request.Payout < 0 || request.Payout > reservation.Stake * 10 || decimal.Round(request.Payout, 2) != request.Payout) throw new DomainException("Invalid payout.");
        if (reservation.Status == ReservationStatus.Settled)
        {
            if (reservation.Payout != request.Payout) throw new DomainException("Settlement payout mismatch.", 409);
            return Decode(reservation.Result);
        }
        var wallet = await Find(db, Guid.Parse(reservation.Player), ct);
        wallet.Total += request.Payout - reservation.Stake;
        wallet.Held -= reservation.Stake;
        var result = new Operation(OperationStatus.Settled, BalanceOf(wallet));
        reservation.Status = ReservationStatus.Settled;
        reservation.Payout = request.Payout;
        reservation.Result = JsonSerializer.Serialize(result);
        return result;
    }, ct);
}
