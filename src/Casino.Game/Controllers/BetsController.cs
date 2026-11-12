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
    public async Task<ActionResult<Round>> Place([FromBody] MoneyRequest request, CancellationToken ct)
    {
        Round round;
        try
        {
            round = await engine.Place(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException or TaskCanceledException))
        {
            round = await store.GetOrCreate(request, ct);
            logger.LogWarning(ex, "Round {RoundId} awaits Wallet; recovery will retry", round.Id);
        }

        if (round.State is RoundState.Settled or RoundState.Rejected)
            return Ok(round);

        return Accepted($"/rounds/{round.Id}", round);
    }
}
