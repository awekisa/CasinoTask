using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Clients;
using Casino.Wallet.Persistence;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class YieldingWallet(WalletStore store) : IWalletClient
{
    public async Task<Operation> Reserve(ReserveRequest request, CancellationToken ct) { await Task.Yield(); return store.Reserve(request); }
    public async Task<Operation> Settle(SettleRequest request, CancellationToken ct) { await Task.Yield(); return store.Settle(request); }
}
