using Casino.Web.Clients;
using Casino.Web.Configuration.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class WalletProxyController(ApiProxy proxy, WalletApiSettings walletSettings) : ControllerBase
{
    [AcceptVerbs("GET", "PUT")]
    [Route("wallets/{playerId:guid}")]
    public Task Wallet(Guid playerId) =>
        proxy.Forward(HttpContext, "Wallet", $"{walletSettings.WalletsPath.TrimEnd('/')}/{playerId}");

    [HttpPost("deposits")]
    public Task Deposit() => proxy.Forward(HttpContext, "Wallet", walletSettings.DepositsPath);

    [HttpPost("withdrawals")]
    public Task Withdraw() => proxy.Forward(HttpContext, "Wallet", walletSettings.WithdrawalsPath);
}
