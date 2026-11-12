
namespace Casino.Contracts.Wallet.Requests;

public record SettleRequest
{
    public SettleRequest(Guid roundId, decimal payout)
    {
        RoundId = roundId;
        Payout = payout;
    }

    public Guid RoundId { get; init; }
    public decimal Payout { get; init; }
}
