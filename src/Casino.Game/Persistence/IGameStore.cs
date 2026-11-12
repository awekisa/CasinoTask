using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;

namespace Casino.Game.Persistence;

public interface IGameStore
{
    Round GetOrCreate(MoneyRequest request);
    Round Get(Guid id);
    Guid[] Unfinished();
    void Decide(Guid id, decimal payout);
    void Reject(Guid id, Operation result);
    void Complete(Guid id, Balance balance);
}
