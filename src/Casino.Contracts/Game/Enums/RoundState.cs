
using System.Text.Json.Serialization;

namespace Casino.Contracts.Game.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<RoundState>))]
public enum RoundState
{
    Pending = 0,
    Decided = 1,
    Settled = 2,
    Rejected = 3,
}
