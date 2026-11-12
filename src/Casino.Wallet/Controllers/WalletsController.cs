using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
[Route("wallets")]
public sealed class WalletsController(WalletStore store) : ControllerBase
{
    [HttpPut("{player:guid}")]
    public ActionResult<Balance> Create(Guid player) => Ok(store.Create(player));

    [HttpGet("{player:guid}")]
    public ActionResult<Balance> Get(Guid player) => Ok(store.Get(player));
}
