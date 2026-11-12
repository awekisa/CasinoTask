using Casino.Web.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class WalletProxyController(ApiProxy proxy) : ControllerBase
{
    private const string WalletsPath = "wallets";
    private const string DepositsPath = "deposits";
    private const string WithdrawalsPath = "withdrawals";

    [AcceptVerbs("GET", "PUT")]
    [Route("wallets/{playerId:guid}")]
    public Task Wallet(Guid playerId) =>
        proxy.Forward(HttpContext, "Wallet", $"{WalletsPath}/{playerId}");

    [HttpPost("deposits")]
    public Task Deposit() => proxy.Forward(HttpContext, "Wallet", DepositsPath);

    [HttpPost("withdrawals")]
    public Task Withdraw() => proxy.Forward(HttpContext, "Wallet", WithdrawalsPath);
}
