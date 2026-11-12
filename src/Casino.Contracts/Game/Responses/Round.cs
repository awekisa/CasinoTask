using Casino.Contracts.Game.Enums;
using Casino.Contracts.Wallet.Responses;

namespace Casino.Contracts.Game.Responses;

public record Round
{
    public Round(Guid id, Guid playerId, Guid requestId, decimal stake, RoundState state, decimal? payout, Balance? balance, string? error)
    {
        Id = id;
        PlayerId = playerId;
        RequestId = requestId;
        Stake = stake;
        State = state;
        Payout = payout;
        Balance = balance;
        Error = error;
    }

    public Guid Id { get; init; }
    public Guid PlayerId { get; init; }
    public Guid RequestId { get; init; }
    public decimal Stake { get; init; }
    public RoundState State { get; init; }
    public decimal? Payout { get; init; }
    public Balance? Balance { get; init; }
    public string? Error { get; init; }
}
