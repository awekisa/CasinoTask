using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
[Route("internal")]
public sealed class ReservationsController(WalletStore store) : ControllerBase
{
    [HttpPost("reservations")]
    public ActionResult<Operation> Reserve([FromBody] ReserveRequest request) => Ok(store.Reserve(request));

    [HttpPost("settlements")]
    public ActionResult<Operation> Settle([FromBody] SettleRequest request) => Ok(store.Settle(request));
}
