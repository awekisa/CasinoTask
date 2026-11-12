
using System.Text.Json.Serialization;

namespace Casino.Contracts.Wallet.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<OperationStatus>))]
public enum OperationStatus
{
    Completed = 0,
    Held = 1,
    Settled = 2,
    Rejected = 3,
}
