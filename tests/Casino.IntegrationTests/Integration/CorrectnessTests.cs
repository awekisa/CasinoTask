using Casino.IntegrationTests.Hosting;
using Casino.Game.Persistence.Implementation;
using Casino.Game.Rounds;
using Casino.Contracts.Game.Enums;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Errors.Exceptions;
using GameDomainException = Casino.Game.Errors.Exceptions.DomainException;
using Casino.Wallet.Finance;
using Casino.Game.Persistence;
using Casino.IntegrationTests.TestDoubles;
using Casino.Wallet.Persistence;

using Xunit;

namespace Casino.IntegrationTests.Integration;

public sealed class CorrectnessTests : IDisposable
{
    private readonly PostgresTestDatabases databases = new();
    private string WalletConnectionString => databases.Wallet;
    private string GameConnectionString => databases.Game;
    private readonly Guid player = Guid.NewGuid();
    private WalletStore Fund(decimal amount = 10)
    {
        var wallet = new WalletStore(WalletConnectionString, 15);
        wallet.Create(player);
        wallet.Change(new(player, Guid.NewGuid(), amount), "deposit");
        return wallet;
    }

    public void Dispose()
    {
        databases.Dispose();
    }

    [Fact]
    public async Task DecimalAmountsRemainExactAcrossRestartAndReplay()
    {
        var wallet = Fund(0.10m);
        var deposit = new MoneyRequest(player, Guid.NewGuid(), 1.20m);
        var deposited = wallet.Change(deposit, "deposit");
        Assert.Equal(new Balance(1.30m, 0m), deposited.Balance);
        wallet = new WalletStore(WalletConnectionString, 15);
        Assert.Equal(deposited, wallet.Change(deposit, "deposit"));
        var request = new MoneyRequest(player, Guid.NewGuid(), 1.01m);
        var engine = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(wallet), new FixedOutcome(0.02m));
        var result = await engine.Place(request);
        Assert.Equal(1.01m, result.Stake);
        Assert.Equal(0.02m, result.Payout);
        Assert.Equal(new Balance(0.31m, 0m), result.Balance);
        var restarted = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(new WalletStore(WalletConnectionString, 15)), new BrokenOutcome());
        Assert.Equal(result, await restarted.Place(request));
        Assert.Equal(new Balance(0.31m, 0m), wallet.Get(player));
    }

    [Fact]
    public void NewWalletAndRepeatedCreationPreserveFunds()
    {
        var wallet = new WalletStore(WalletConnectionString, 15);
        Assert.Equal(new Balance(0, 0), wallet.Create(player));
        wallet.Change(new(player, Guid.NewGuid(), 10), "deposit");
        Assert.Equal(10.0m, wallet.Create(player).Total);
        Assert.Equal(10.0m, new WalletStore(WalletConnectionString, 15).Get(player).Total);
    }

    [Fact]
    public void InvalidMoneyAndEmptyIdsAreRejected()
    {
        var wallet = Fund();
        foreach (var amount in new[] { 0m, -1m, 1.001m, decimal.MaxValue })
            Assert.Throws<DomainException>(() => wallet.Change(new(player, Guid.NewGuid(), amount), "withdraw"));
        Assert.Throws<DomainException>(() => wallet.Create(Guid.Empty));
        Assert.Equal(new Balance(10.0m, 0), wallet.Get(player));
    }

    [Fact]
    public async Task ConcurrentWithdrawalsAndReservationsCannotOverspend()
    {
        var wallet = Fund();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => i % 2 == 0
            ? wallet.Change(new(player, Guid.NewGuid(), 1), "withdraw")
            : wallet.Reserve(new(player, Guid.NewGuid(), 1m)))));
        Assert.Equal(10, results.Count(x => x.Status != OperationStatus.Rejected));
        var balance = wallet.Get(player);
        Assert.Equal(0, balance.Available);
        Assert.Equal(10.0m - results.Count(x => x.Status == OperationStatus.Completed) * 1m, balance.Total);
        Assert.Equal(results.Count(x => x.Status == OperationStatus.Held) * 1m, balance.Held);
    }

    [Fact]
    public async Task DepositAndWithdrawalKeysAreAtomicAndPayloadBound()
    {
        var wallet = Fund();
        var request = new MoneyRequest(player, Guid.NewGuid(), 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => wallet.Change(request, "withdraw"))));
        Assert.All(results, x => Assert.Equal(results[0], x));
        Assert.Equal(9.0m, wallet.Get(player).Total);
        Assert.Equal(409, Assert.Throws<DomainException>(() => wallet.Change(request with { Amount = 2 }, "withdraw")).Status);
        Assert.Throws<DomainException>(() => wallet.Change(request, "deposit"));
        var deposit = request with { RequestId = Guid.NewGuid() };
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => wallet.Change(deposit, "deposit"))));
        Assert.Equal(10.0m, wallet.Get(player).Total);
    }

    [Fact]
    public async Task ReservationAndSettlementDuplicatesHaveOneEffect()
    {
        var wallet = Fund();
        var request = new ReserveRequest(player, Guid.NewGuid(), 5.0m);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => wallet.Reserve(request))));
        Assert.Equal(new Balance(10.0m, 5.0m), wallet.Get(player));
        Assert.Throws<DomainException>(() => wallet.Reserve(request with { Stake = 6.0m }));
        var settlements = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => wallet.Settle(new(request.RoundId, 8.5m)))));
        Assert.All(settlements, x => Assert.Equal(new Balance(13.5m, 0), x.Balance));
        Assert.Equal(new Balance(13.5m, 0), wallet.Get(player));
        Assert.Equal(OperationStatus.Settled, wallet.Reserve(request).Status);
        Assert.Throws<DomainException>(() => wallet.Settle(new(request.RoundId, 9.0m)));
        Assert.Throws<DomainException>(() => wallet.Settle(new(Guid.NewGuid(), 8.5m)));
    }

    [Fact]
    public void RejectionsRemainRejectedAfterDeposit()
    {
        var wallet = Fund(1);
        var withdraw = new MoneyRequest(player, Guid.NewGuid(), 5);
        var reserve = new ReserveRequest(player, Guid.NewGuid(), 5.0m);
        Assert.Equal(OperationStatus.Rejected, wallet.Change(withdraw, "withdraw").Status);
        Assert.Equal(OperationStatus.Rejected, wallet.Reserve(reserve).Status);
        wallet.Change(new(player, Guid.NewGuid(), 10), "deposit");
        Assert.Equal(OperationStatus.Rejected, wallet.Change(withdraw, "withdraw").Status);
        Assert.Equal(OperationStatus.Rejected, wallet.Reserve(reserve).Status);
        Assert.Throws<DomainException>(() => wallet.Settle(new(reserve.RoundId, 0)));
        Assert.Equal(new Balance(11.0m, 0), wallet.Get(player));
    }

    [Theory]
    [InlineData("reserve-before")]
    [InlineData("reserve-after")]
    [InlineData("settle-before")]
    [InlineData("settle-after")]
    public async Task LostResponsesAndOutagesRecoverAcrossStoreRestart(string failure)
    {
        var wallet = Fund();
        var store = new GameStore(GameConnectionString, 15);
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        var engine = new GameEngine(store, new FaultWallet(wallet, failure), new FixedOutcome(8.5m));
        await Assert.ThrowsAsync<HttpRequestException>(() => engine.Place(request));
        var id = Assert.Single(store.Unfinished());
        // Recreate both repositories and engine: no in-memory recovery state survives.
        var restarted = new GameStore(GameConnectionString, 15);
        var restartedWallet = new WalletStore(WalletConnectionString, 15);
        var recovery = new GameEngine(restarted, new FaultWallet(restartedWallet), new FixedOutcome(failure.StartsWith("settle") ? 1.23m : 8.5m));
        var result = await recovery.Advance(id);
        Assert.Equal(RoundState.Settled, result.State);
        Assert.Equal(8.5m, result.Payout);
        Assert.Equal(new Balance(13.5m, 0), restartedWallet.Get(player));
        Assert.Equal(result, await recovery.Advance(id));
        Assert.Empty(restarted.Unfinished());
        Assert.Equal(result, await recovery.Place(request));
    }

    [Fact]
    public async Task OutcomeFailureAfterReservationRecoversWithoutDoubleHold()
    {
        var wallet = Fund();
        var store = new GameStore(GameConnectionString, 15);
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GameEngine(store, new FaultWallet(wallet), new BrokenOutcome()).Place(request));
        Assert.Equal(new Balance(10.0m, 5.0m), wallet.Get(player));
        var result = await new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(new WalletStore(WalletConnectionString, 15)), new FixedOutcome(8.5m)).Place(request);
        Assert.Equal(RoundState.Settled, result.State);
        Assert.Equal(new Balance(13.5m, 0), wallet.Get(player));
    }

    [Fact]
    public async Task ConcurrentGameWorkersSettleOnlyTheWinningPersistedOutcome()
    {
        var wallet = Fund();
        var store = new GameStore(GameConnectionString, 15);
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        var engine = new GameEngine(store, new YieldingWallet(wallet), new VaryingOutcome());
        var rounds = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => engine.Place(request))));
        Assert.Single(rounds.Select(r => r.Id).Distinct());
        Assert.Single(rounds.Select(r => r.Payout).Distinct());
        Assert.All(rounds, r => Assert.Equal(RoundState.Settled, r.State));
        Assert.Equal(new Balance(5.0m + rounds[0].Payout!.Value, 0), wallet.Get(player));
        await Assert.ThrowsAsync<GameDomainException>(() => engine.Place(request with { Amount = 6 }));
    }

    [Fact]
    public async Task PendingRoundBeforeWalletCallIsRecoverableAndUnknownPlayerIsRejected()
    {
        var wallet = Fund();
        var store = new GameStore(GameConnectionString, 15);
        var pending = store.GetOrCreate(new(player, Guid.NewGuid(), 5));
        var engine = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(wallet), new FixedOutcome(8.5m));
        Assert.Equal(RoundState.Settled, (await engine.Advance(pending.Id)).State);
        var unknown = await engine.Place(new(Guid.NewGuid(), Guid.NewGuid(), 5));
        Assert.Equal(RoundState.Rejected, unknown.State);
    }

    [Fact]
    public async Task AssessmentExampleAndReplayBalance()
    {
        var wallet = Fund();
        var engine = new GameEngine(new GameStore(GameConnectionString, 15), new FaultWallet(wallet), new FixedOutcome(8.5m));
        var request = new MoneyRequest(player, Guid.NewGuid(), 5);
        var round = await engine.Place(request);
        Assert.Equal(new Balance(13.5m, 0), round.Balance);
        Assert.Equal(new Balance(0, 0), wallet.Change(new(player, Guid.NewGuid(), 13.5m), "withdraw").Balance);
        Assert.Equal(new Balance(13.5m, 0), (await engine.Place(request)).Balance);
        Assert.Equal(new Balance(0, 0), wallet.Get(player));
    }

    [Fact]
    public void InvalidPayoutDoesNotConsumeReservationAndDepositsPreservePayoutHeadroom()
    {
        var wallet = Fund();
        var id = Guid.NewGuid();
        wallet.Reserve(new(player, id, 5.0m));
        Assert.Throws<DomainException>(() => wallet.Settle(new(id, 0.001m)));
        Assert.Throws<DomainException>(() => wallet.Settle(new(id, -1)));
        Assert.Throws<DomainException>(() => wallet.Settle(new(id, 50.01m)));
        Assert.Equal(new Balance(10.0m, 5.0m), wallet.Get(player));
        Assert.Equal(OperationStatus.Rejected, wallet.Change(new(player, Guid.NewGuid(), Money.MaxBalance - 10), "deposit").Status);
        wallet.Settle(new(id, 50.0m));
        Assert.Equal(new Balance(55.0m, 0), wallet.Get(player));
    }
}
