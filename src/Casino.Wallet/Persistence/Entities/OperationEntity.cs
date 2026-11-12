using Casino.Wallet.Persistence.Enums;

namespace Casino.Wallet.Persistence.Entities;

public sealed class OperationEntity
{
    public string Player { get; set; } = "";
    public string Request { get; set; } = "";
    public MoneyOperationKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Result { get; set; } = "";
}
