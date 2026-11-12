using Casino.Web.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class GameProxyController(ApiProxy proxy) : ControllerBase
{
    private const string BetsPath = "bets";

    [HttpPost("bets")]
    public Task PlaceBet() => proxy.Forward(HttpContext, "Game", BetsPath);
}
