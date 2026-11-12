using Casino.Wallet.Wallets;
using Casino.Contracts.Wallet.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Wallet.Controllers;

[ApiController]
[Route("wallets")]
public sealed class WalletsController(WalletService store) : ControllerBase
{
    [HttpPut("{player:guid}")]
    public async Task<ActionResult<Balance>> Create(Guid player, CancellationToken ct) => Ok(await store.Create(player, ct));

    [HttpGet("{player:guid}")]
    public async Task<ActionResult<Balance>> Get(Guid player, CancellationToken ct) => Ok(await store.Get(player, ct));
}
