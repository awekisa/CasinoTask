
namespace Casino.Contracts.Wallet.Responses;

public record Balance
{
    public Balance(decimal total, decimal held)
    {
        Total = total;
        Held = held;
    }

    public decimal Total { get; init; }
    public decimal Held { get; init; }
    public decimal Available => Total - Held;
}
