using Casino.Contracts.Wallet.Enums;

namespace Casino.Contracts.Wallet.Responses;

public record Operation
{
    public Operation(OperationStatus status, Balance balance, string? error = null)
    {
        Status = status;
        Balance = balance;
        Error = error;
    }

    public OperationStatus Status { get; init; }
    public Balance Balance { get; init; }
    public string? Error { get; init; }
}
