using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Requests;
using Casino.Game.Persistence;
using Casino.Game.Rounds;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Game.Controllers;

[ApiController]
[Route("bets")]
public sealed class BetsController(IGameStore store, GameEngine engine, ILogger<BetsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Round>> Place([FromBody] MoneyRequest request)
    {
        var round = store.GetOrCreate(request);
        try
        {
            round = await engine.Advance(round.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Round {RoundId} awaits Wallet; recovery will retry", round.Id);
            round = store.Get(round.Id);
        }

        if (round.State is RoundState.Settled or RoundState.Rejected)
            return Ok(round);

        return Accepted($"/rounds/{round.Id}", round);
    }
}
