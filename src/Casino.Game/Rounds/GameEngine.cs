using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Requests;
using Casino.Game.Clients;
using Casino.Game.Persistence;

namespace Casino.Game.Rounds;

public sealed class GameEngine(IGameStore store, IWalletClient wallet, IOutcome outcome)
{
    public async Task<Round> Place(MoneyRequest request, CancellationToken ct = default)
    {
        var round = await store.GetOrCreate(request, ct);
        return await Advance(round.Id, ct);
    }

    public async Task<Round> Advance(Guid id, CancellationToken ct = default)
    {
        var round = await store.Get(id, ct);
        if (round.State == RoundState.Pending)
        {
            var reserved = await wallet.Reserve(new(round.PlayerId, round.Id, round.Stake), ct);
            if (reserved.Status == OperationStatus.Rejected) { await store.Reject(id, reserved, ct); return await store.Get(id, ct); }
            if (reserved.Status is not (OperationStatus.Held or OperationStatus.Settled)) throw new HttpRequestException("Unexpected reservation state.");
            await store.Decide(id, outcome.Draw(round.Stake), ct);
            // A concurrent worker may have won the write. Always use its persisted outcome.
            round = await store.Get(id, ct);
        }

        if (round.State == RoundState.Decided)
        {
            var settled = await wallet.Settle(new(id, round.Payout!.Value), ct);
            if (settled.Status != OperationStatus.Settled) throw new HttpRequestException("Unexpected settlement state.");
            await store.Complete(id, settled.Balance, ct);
        }

        return await store.Get(id, ct);
    }
}
