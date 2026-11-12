using Casino.Contracts.Game.Enums;

namespace Casino.Game.Persistence.Entities;

public sealed class RoundEntity
{
    public string Id { get; set; } = "";
    public string Player { get; set; } = "";
    public string Request { get; set; } = "";
    public decimal Stake { get; set; }
    public RoundState State { get; set; } = RoundState.Pending;
    public decimal? Payout { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
