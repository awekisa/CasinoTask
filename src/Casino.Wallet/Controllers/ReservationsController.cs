using Casino.Wallet.Wallets;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
[Route("internal")]
public sealed class ReservationsController(WalletService store) : ControllerBase
{
    [HttpPost("reservations")]
    public async Task<ActionResult<Operation>> Reserve([FromBody] ReserveRequest request, CancellationToken ct) => Ok(await store.Reserve(request, ct));

    [HttpPost("settlements")]
    public async Task<ActionResult<Operation>> Settle([FromBody] SettleRequest request, CancellationToken ct) => Ok(await store.Settle(request, ct));
}
