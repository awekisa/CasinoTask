using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using System.Text.Json;
using Xunit;

namespace Casino.Game.UnitTests.Serialization;

public sealed class StatusSerializationTests
{
    [Theory]
    [InlineData(RoundState.Pending, "Pending")]
    [InlineData(RoundState.Decided, "Decided")]
    [InlineData(RoundState.Settled, "Settled")]
    [InlineData(RoundState.Rejected, "Rejected")]
    public void RoundStatePreservesExistingJsonContract(RoundState state, string name)
    {
        var round = new Round(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m, state, null, null, null);
        var encoded = JsonSerializer.Serialize(round);
        using var json = JsonDocument.Parse(encoded);
        Assert.Equal(name, json.RootElement.GetProperty("State").GetString());
        Assert.Equal(round, JsonSerializer.Deserialize<Round>(encoded));
    }

    [Fact]
    public void UnknownStatusNamesAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RoundState>("\"Paid\""));
    }
}
