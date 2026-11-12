
namespace Casino.Contracts.Wallet.Requests;

public record MoneyRequest
{
    public MoneyRequest(Guid playerId, Guid requestId, decimal amount)
    {
        PlayerId = playerId;
        RequestId = requestId;
        Amount = amount;
    }

    public Guid PlayerId { get; init; }
    public Guid RequestId { get; init; }
    public decimal Amount { get; init; }
}
