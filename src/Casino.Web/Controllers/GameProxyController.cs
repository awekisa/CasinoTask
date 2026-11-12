using Casino.Web.Clients;
using Casino.Web.Configuration.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class GameProxyController(ApiProxy proxy, GameApiSettings gameSettings) : ControllerBase
{
    [HttpPost("bets")]
    public Task PlaceBet() => proxy.Forward(HttpContext, "Game", gameSettings.BetsPath);
}
