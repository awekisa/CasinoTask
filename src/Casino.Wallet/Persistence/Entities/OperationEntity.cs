
namespace Casino.Wallet.Persistence.Entities;

public sealed class OperationEntity
{
    public string Player { get; set; } = "";
    public string Request { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public string Result { get; set; } = "";
}
