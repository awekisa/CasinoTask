using Casino.Wallet.Wallets;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Clients;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class YieldingWallet(WalletService store) : IWalletClient
{
    public async Task<Operation> Reserve(ReserveRequest request, CancellationToken ct)
    {
        await Task.Yield();
        return await store.Reserve(request, ct);
    }

    public async Task<Operation> Settle(SettleRequest request, CancellationToken ct)
    {
        await Task.Yield();
        return await store.Settle(request, ct);
    }
}
