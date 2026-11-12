using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Errors.Exceptions;
using Casino.Wallet.Finance;
using Casino.Wallet.Persistence;
using Casino.Wallet.Persistence.Entities;
using Casino.Wallet.Persistence.Enums;
using Moq;
using Xunit;

namespace Casino.Wallet.UnitTests.Persistence;

public sealed class WalletStoreTests
{
    private readonly Guid playerId = Guid.NewGuid();
    private readonly Mock<IWalletSession> session = new(MockBehavior.Strict);
    private readonly Mock<IWalletDatabase> database = new(MockBehavior.Strict);
    private readonly WalletEntity account;
    private OperationEntity? operation;
    private ReservationEntity? reservation;
    private WalletStore Store => new(database.Object);

    public WalletStoreTests()
    {
        account = new WalletEntity { Player = playerId, Total = 10m };
        session.Setup(s => s.FindWallet(playerId, default)).ReturnsAsync(account);
        session.Setup(s => s.FindOperation(playerId, It.IsAny<Guid>(), default)).ReturnsAsync(() => operation);
        session.Setup(s => s.FindReservation(It.IsAny<Guid>(), default)).ReturnsAsync(() => reservation);
        session.Setup(s => s.AddOperation(It.IsAny<OperationEntity>())).Callback<OperationEntity>(value => operation = value);
        session.Setup(s => s.AddReservation(It.IsAny<ReservationEntity>())).Callback<ReservationEntity>(value => reservation = value);
        database.Setup(d => d.Write(It.IsAny<Func<IWalletSession, Task<Operation>>>(), default))
            .Returns((Func<IWalletSession, Task<Operation>> action, CancellationToken ct) => action(session.Object));
        database.Setup(d => d.Write(It.IsAny<Func<IWalletSession, Task<Balance>>>(), default))
            .Returns((Func<IWalletSession, Task<Balance>> action, CancellationToken ct) => action(session.Object));
        database.Setup(d => d.Read(It.IsAny<Func<IWalletSession, Task<Balance>>>(), default))
            .Returns((Func<IWalletSession, Task<Balance>> action, CancellationToken ct) => action(session.Object));
    }

    [Theory]
    [InlineData(MoneyOperationKind.Deposit, "10.15")]
    [InlineData(MoneyOperationKind.Withdraw, "9.85")]
    public async Task MoneyChangesAreExactAndReplaysDoNotApplyAgain(MoneyOperationKind kind, string expected)
    {
        var request = new MoneyRequest(playerId, Guid.NewGuid(), 0.15m);
        var result = await Store.Change(request, kind);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), account.Total);
        Assert.Equal(result, await Store.Change(request, kind));
        session.Verify(s => s.AddOperation(It.IsAny<OperationEntity>()), Times.Once);
        Assert.Equal(result.Balance.Total, account.Total);
    }

    [Fact]
    public async Task HeldMoneyCannotBeWithdrawn()
    {
        account.Held = 8m;
        var result = await Store.Change(new(playerId, Guid.NewGuid(), 3m), MoneyOperationKind.Withdraw);
        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(new Balance(10m, 8m), result.Balance);
        Assert.Equal(10m, account.Total);
    }

    [Fact]
    public async Task IdempotencyKeyCannotBeReusedWithDifferentAmount()
    {
        var request = new MoneyRequest(playerId, Guid.NewGuid(), 1m);
        await Store.Change(request, MoneyOperationKind.Deposit);
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => Store.Change(request with { Amount = 2m }, MoneyOperationKind.Deposit))).Status);
        Assert.Equal(11m, account.Total);
        session.Verify(s => s.AddOperation(It.IsAny<OperationEntity>()), Times.Once);
    }

    [Fact]
    public async Task ReserveAndSettleApplyOnceAndReplayOriginalBalance()
    {
        var request = new ReserveRequest(playerId, Guid.NewGuid(), 5m);
        var held = await Store.Reserve(request);
        Assert.Equal(new Balance(10m, 5m), held.Balance);
        Assert.Equal(held, await Store.Reserve(request));
        var result = await Store.Settle(new(request.RoundId, 8.50m));
        Assert.Equal(new Balance(13.50m, 0), result.Balance);
        Assert.Equal(ReservationStatus.Settled, reservation!.Status);
        account.Total = 20m; // A later operation must not alter the recorded replay response.
        Assert.Equal(result, await Store.Settle(new(request.RoundId, 8.50m)));
        Assert.Equal(20m, account.Total);
        await Assert.ThrowsAsync<DomainException>(() => Store.Settle(new(request.RoundId, 9m)));
        session.Verify(s => s.AddReservation(It.IsAny<ReservationEntity>()), Times.Once);
    }

    [Fact]
    public async Task RejectedReservationStaysRejectedAfterFundsArrive()
    {
        account.Total = 1m;
        var request = new ReserveRequest(playerId, Guid.NewGuid(), 5m);
        Assert.Equal(OperationStatus.Rejected, (await Store.Reserve(request)).Status);
        account.Total = 10m;
        Assert.Equal(OperationStatus.Rejected, (await Store.Reserve(request)).Status);
        await Assert.ThrowsAsync<DomainException>(() => Store.Settle(new(request.RoundId, 0m)));
        Assert.Equal(0m, account.Held);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("50.01")]
    public async Task InvalidPayoutPreservesHeldFunds(string input)
    {
        var roundId = Guid.NewGuid();
        await Store.Reserve(new(playerId, roundId, 5m));
        var payout = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<DomainException>(() => Store.Settle(new(roundId, payout)));
        Assert.Equal(ReservationStatus.Held, reservation!.Status);
        Assert.Equal(new Balance(10m, 5m), await Store.Get(playerId));
    }

    [Fact]
    public async Task DepositPreservesHeadroomForHeldBets()
    {
        account.Held = 5m;
        var result = await Store.Change(new(playerId, Guid.NewGuid(), Money.MaxBalance - 10m), MoneyOperationKind.Deposit);
        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(10m, account.Total);
    }

    [Fact]
    public async Task InvalidAmountIsRejectedBeforeCallingDatabase()
    {
        await Assert.ThrowsAsync<DomainException>(() => Store.Change(new(playerId, Guid.NewGuid(), 0.001m), MoneyOperationKind.Deposit));
        database.VerifyNoOtherCalls();
        session.VerifyNoOtherCalls();
    }
}
