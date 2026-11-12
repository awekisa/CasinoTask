using Casino.Contracts.Game.Responses;
using Casino.Game.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Casino.Game.Controllers;

[ApiController]
[Route("rounds")]
public sealed class RoundsController(IGameStore store) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Round>> Get(Guid id, CancellationToken ct) => Ok(await store.Get(id, ct));
}
