using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;

namespace Casino.Game.Clients;

public interface IWalletClient
{
    Task<Operation> Reserve(ReserveRequest request, CancellationToken ct);
    Task<Operation> Settle(SettleRequest request, CancellationToken ct);
}
