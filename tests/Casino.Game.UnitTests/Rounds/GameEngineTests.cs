using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Clients;
using Casino.Game.Persistence;
using Casino.Game.Rounds;
using Moq;
using Xunit;

namespace Casino.Game.UnitTests.Rounds;

public sealed class GameEngineTests
{
    private readonly Mock<IGameStore> store = new(MockBehavior.Strict);
    private readonly Mock<IWalletClient> wallet = new(MockBehavior.Strict);
    private readonly Mock<IOutcome> outcome = new(MockBehavior.Strict);
    private readonly Round pending = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m, RoundState.Pending, null, null, null);
    private GameEngine Engine => new(store.Object, wallet.Object, outcome.Object);

    [Fact]
    public async Task PlaceReservesThenSettlesThePersistedOutcome()
    {
        var request = new MoneyRequest(pending.PlayerId, pending.RequestId, pending.Stake);
        var decided = pending with { State = RoundState.Decided, Payout = 8.50m };
        var balance = new Balance(13.50m, 0);
        var settled = decided with { State = RoundState.Settled, Balance = balance };
        store.Setup(s => s.GetOrCreate(request, default)).ReturnsAsync(pending);
        store.SetupSequence(s => s.Get(pending.Id, default)).ReturnsAsync(pending).ReturnsAsync(decided).ReturnsAsync(settled);
        wallet.Setup(w => w.Reserve(new ReserveRequest(pending.PlayerId, pending.Id, 5m), default))
            .ReturnsAsync(new Operation(OperationStatus.Held, new Balance(10m, 5m)));
        outcome.Setup(o => o.Draw(5m)).Returns(3m);
        store.Setup(s => s.Decide(pending.Id, 3m, default)).Returns(Task.CompletedTask);
        // Another worker's decision won: use 8.50 from storage, not our generated 3.
        wallet.Setup(w => w.Settle(new SettleRequest(pending.Id, 8.50m), default))
            .ReturnsAsync(new Operation(OperationStatus.Settled, balance));
        store.Setup(s => s.Complete(pending.Id, balance, default)).Returns(Task.CompletedTask);

        Assert.Equal(settled, await Engine.Place(request));
        wallet.VerifyAll();
        store.VerifyAll();
        outcome.VerifyAll();
    }

    [Fact]
    public async Task DecidedRoundResumesSettlementWithoutDrawingAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var decided = pending with { State = RoundState.Decided, Payout = 0m };
        var balance = new Balance(5m, 0);
        var settled = decided with { State = RoundState.Settled, Balance = balance };
        store.SetupSequence(s => s.Get(pending.Id, ct)).ReturnsAsync(decided).ReturnsAsync(settled);
        wallet.Setup(w => w.Settle(new SettleRequest(pending.Id, 0m), ct))
            .ReturnsAsync(new Operation(OperationStatus.Settled, balance));
        store.Setup(s => s.Complete(pending.Id, balance, ct)).Returns(Task.CompletedTask);
        Assert.Equal(settled, await Engine.Advance(pending.Id, ct));
        outcome.VerifyNoOtherCalls();
        wallet.Verify(w => w.Reserve(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReservationRejectionStopsBeforeDrawingOrSettling()
    {
        var rejection = new Operation(OperationStatus.Rejected, new Balance(0, 0), "Insufficient funds.");
        var rejected = pending with { State = RoundState.Rejected, Error = rejection.Error };
        store.SetupSequence(s => s.Get(pending.Id, default)).ReturnsAsync(pending).ReturnsAsync(rejected);
        wallet.Setup(w => w.Reserve(It.IsAny<ReserveRequest>(), default)).ReturnsAsync(rejection);
        store.Setup(s => s.Reject(pending.Id, rejection, default)).Returns(Task.CompletedTask);
        Assert.Equal(rejected, await Engine.Advance(pending.Id));
        outcome.VerifyNoOtherCalls();
        wallet.Verify(w => w.Settle(It.IsAny<SettleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(RoundState.Settled)]
    [InlineData(RoundState.Rejected)]
    public async Task FinishedRoundReplayHasNoSideEffects(RoundState state)
    {
        var completed = pending with { State = state };
        store.Setup(s => s.Get(pending.Id, default)).ReturnsAsync(completed);
        Assert.Equal(completed, await Engine.Advance(pending.Id));
        wallet.VerifyNoOtherCalls();
        outcome.VerifyNoOtherCalls();
        store.Verify(s => s.Get(pending.Id, default), Times.Exactly(2));
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostReservationResponseDoesNotDecideOrRejectRound()
    {
        store.Setup(s => s.Get(pending.Id, default)).ReturnsAsync(pending);
        wallet.Setup(w => w.Reserve(It.IsAny<ReserveRequest>(), default)).ThrowsAsync(new HttpRequestException("Lost response"));
        await Assert.ThrowsAsync<HttpRequestException>(() => Engine.Advance(pending.Id));
        store.Verify(s => s.Get(pending.Id, default), Times.Once);
        store.VerifyNoOtherCalls();
        outcome.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LostSettlementResponseDoesNotCompleteRound()
    {
        store.Setup(s => s.Get(pending.Id, default)).ReturnsAsync(pending with { State = RoundState.Decided, Payout = 8m });
        wallet.Setup(w => w.Settle(new SettleRequest(pending.Id, 8m), default)).ThrowsAsync(new HttpRequestException("Lost response"));
        await Assert.ThrowsAsync<HttpRequestException>(() => Engine.Advance(pending.Id));
        store.Verify(s => s.Get(pending.Id, default), Times.Once);
        store.VerifyNoOtherCalls();
        outcome.VerifyNoOtherCalls();
    }
}
