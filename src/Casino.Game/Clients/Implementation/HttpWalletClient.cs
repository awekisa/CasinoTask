using Casino.Game.Configuration.Settings;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Clients;

using System.Net.Http.Json;

namespace Casino.Game.Clients.Implementation;

public sealed class HttpWalletClient(HttpClient client, WalletClientSettings walletSettings) : IWalletClient
{
    private async Task<Operation> Send<T>(string path, T request, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(path, request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Operation>(cancellationToken: ct) ?? throw new HttpRequestException("Empty Wallet response.");
    }

    public Task<Operation> Reserve(ReserveRequest request, CancellationToken ct) => Send(walletSettings.ReservationsPath, request, ct);

    public Task<Operation> Settle(SettleRequest request, CancellationToken ct) => Send(walletSettings.SettlementsPath, request, ct);
}
