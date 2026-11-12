
using System.Text.Json.Serialization;

namespace Casino.Wallet.Persistence.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<ReservationStatus>))]
public enum ReservationStatus
{
    Held = 0,
    Settled = 1,
    Rejected = 2,
}
