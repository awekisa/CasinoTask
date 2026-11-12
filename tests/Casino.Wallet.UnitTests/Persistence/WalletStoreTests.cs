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
        account = new WalletEntity { Player = playerId.ToString(), Total = 10m };
        session.Setup(s => s.FindWallet(playerId)).Returns(account);
        session.Setup(s => s.FindOperation(playerId, It.IsAny<Guid>())).Returns(() => operation);
        session.Setup(s => s.FindReservation(It.IsAny<Guid>())).Returns(() => reservation);
        session.Setup(s => s.AddOperation(It.IsAny<OperationEntity>())).Callback<OperationEntity>(value => operation = value);
        session.Setup(s => s.AddReservation(It.IsAny<ReservationEntity>())).Callback<ReservationEntity>(value => reservation = value);
        database.Setup(d => d.Write(It.IsAny<Func<IWalletSession, Operation>>()))
            .Returns((Func<IWalletSession, Operation> action) => action(session.Object));
        database.Setup(d => d.Write(It.IsAny<Func<IWalletSession, Balance>>()))
            .Returns((Func<IWalletSession, Balance> action) => action(session.Object));
        database.Setup(d => d.Read(It.IsAny<Func<IWalletSession, Balance>>()))
            .Returns((Func<IWalletSession, Balance> action) => action(session.Object));
    }

    [Theory]
    [InlineData("deposit", "10.15")]
    [InlineData("withdraw", "9.85")]
    public void MoneyChangesAreExactAndReplaysDoNotApplyAgain(string kind, string expected)
    {
        var request = new MoneyRequest(playerId, Guid.NewGuid(), 0.15m);
        var result = Store.Change(request, kind);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), account.Total);
        Assert.Equal(result, Store.Change(request, kind));
        session.Verify(s => s.AddOperation(It.IsAny<OperationEntity>()), Times.Once);
        Assert.Equal(result.Balance.Total, account.Total);
    }

    [Fact]
    public void HeldMoneyCannotBeWithdrawn()
    {
        account.Held = 8m;
        var result = Store.Change(new(playerId, Guid.NewGuid(), 3m), "withdraw");
        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(new Balance(10m, 8m), result.Balance);
        Assert.Equal(10m, account.Total);
    }

    [Fact]
    public void IdempotencyKeyCannotBeReusedWithDifferentAmount()
    {
        var request = new MoneyRequest(playerId, Guid.NewGuid(), 1m);
        Store.Change(request, "deposit");
        Assert.Equal(409, Assert.Throws<DomainException>(() => Store.Change(request with { Amount = 2m }, "deposit")).Status);
        Assert.Equal(11m, account.Total);
        session.Verify(s => s.AddOperation(It.IsAny<OperationEntity>()), Times.Once);
    }

    [Fact]
    public void ReserveAndSettleApplyOnceAndReplayOriginalBalance()
    {
        var request = new ReserveRequest(playerId, Guid.NewGuid(), 5m);
        var held = Store.Reserve(request);
        Assert.Equal(new Balance(10m, 5m), held.Balance);
        Assert.Equal(held, Store.Reserve(request));
        var result = Store.Settle(new(request.RoundId, 8.50m));
        Assert.Equal(new Balance(13.50m, 0), result.Balance);
        Assert.Equal(ReservationStatus.Settled, reservation!.Status);
        account.Total = 20m; // A later operation must not alter the recorded replay response.
        Assert.Equal(result, Store.Settle(new(request.RoundId, 8.50m)));
        Assert.Equal(20m, account.Total);
        Assert.Throws<DomainException>(() => Store.Settle(new(request.RoundId, 9m)));
        session.Verify(s => s.AddReservation(It.IsAny<ReservationEntity>()), Times.Once);
    }

    [Fact]
    public void RejectedReservationStaysRejectedAfterFundsArrive()
    {
        account.Total = 1m;
        var request = new ReserveRequest(playerId, Guid.NewGuid(), 5m);
        Assert.Equal(OperationStatus.Rejected, Store.Reserve(request).Status);
        account.Total = 10m;
        Assert.Equal(OperationStatus.Rejected, Store.Reserve(request).Status);
        Assert.Throws<DomainException>(() => Store.Settle(new(request.RoundId, 0m)));
        Assert.Equal(0m, account.Held);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("50.01")]
    public void InvalidPayoutPreservesHeldFunds(string input)
    {
        var roundId = Guid.NewGuid();
        Store.Reserve(new(playerId, roundId, 5m));
        var payout = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<DomainException>(() => Store.Settle(new(roundId, payout)));
        Assert.Equal(ReservationStatus.Held, reservation!.Status);
        Assert.Equal(new Balance(10m, 5m), Store.Get(playerId));
    }

    [Fact]
    public void DepositPreservesHeadroomForHeldBets()
    {
        account.Held = 5m;
        var result = Store.Change(new(playerId, Guid.NewGuid(), Money.MaxBalance - 10m), "deposit");
        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(10m, account.Total);
    }

    [Fact]
    public void InvalidAmountIsRejectedBeforeCallingDatabase()
    {
        Assert.Throws<DomainException>(() => Store.Change(new(playerId, Guid.NewGuid(), 0.001m), "deposit"));
        database.VerifyNoOtherCalls();
        session.VerifyNoOtherCalls();
    }
}
