
namespace Casino.Contracts.Wallet.Requests;

public record ReserveRequest
{
    public ReserveRequest(Guid playerId, Guid roundId, decimal stake)
    {
        PlayerId = playerId;
        RoundId = roundId;
        Stake = stake;
    }

    public Guid PlayerId { get; init; }
    public Guid RoundId { get; init; }
    public decimal Stake { get; init; }
}
