
namespace Casino.Wallet.Persistence.Entities;

public sealed class WalletEntity
{
    public string Player { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Held { get; set; }
}
