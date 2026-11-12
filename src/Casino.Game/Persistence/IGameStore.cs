using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;

namespace Casino.Game.Persistence;

public interface IGameStore
{
    Task<Round> GetOrCreate(MoneyRequest request, CancellationToken ct = default);
    Task<Round> Get(Guid id, CancellationToken ct = default);
    Task<Guid[]> Unfinished(CancellationToken ct = default);
    Task Decide(Guid id, decimal payout, CancellationToken ct = default);
    Task Reject(Guid id, Operation result, CancellationToken ct = default);
    Task Complete(Guid id, Balance balance, CancellationToken ct = default);
}
