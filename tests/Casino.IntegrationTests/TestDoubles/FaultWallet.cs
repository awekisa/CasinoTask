using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Clients;
using Casino.Wallet.Persistence;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class FaultWallet(WalletStore store, string? fault = null) : IWalletClient
{
    private bool fired;
    private void Fail(string point)
    {
        if (!fired && fault == point)
        {
            fired = true;
            throw new HttpRequestException("Simulated lost response or outage");
        }
    }

    public async Task<Operation> Reserve(ReserveRequest request, CancellationToken ct)
    {
        Fail("reserve-before");
        var result = await store.Reserve(request, ct);
        Fail("reserve-after");
        return result;
    }

    public async Task<Operation> Settle(SettleRequest request, CancellationToken ct)
    {
        Fail("settle-before");
        var result = await store.Settle(request, ct);
        Fail("settle-after");
        return result;
    }
}
