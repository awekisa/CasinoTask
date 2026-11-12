using Casino.Contracts.Wallet.Enums;
using Casino.Contracts.Wallet.Responses;
using Casino.Wallet.Persistence.Enums;
using System.Text.Json;
using Xunit;

namespace Casino.Wallet.UnitTests.Serialization;

public sealed class StatusSerializationTests
{
    [Theory]
    [InlineData(OperationStatus.Completed, "Completed")]
    [InlineData(OperationStatus.Held, "Held")]
    [InlineData(OperationStatus.Settled, "Settled")]
    [InlineData(OperationStatus.Rejected, "Rejected")]
    public void OperationStatusPreservesExistingJsonContract(OperationStatus status, string name)
    {
        var legacyJson = "{\"Status\":\"" + name + "\",\"Balance\":{\"Total\":10.00,\"Held\":0},\"Error\":null}";
        var operation = JsonSerializer.Deserialize<Operation>(legacyJson)!;
        Assert.Equal(status, operation.Status);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(operation));
        Assert.Equal(name, json.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public void UnknownStatusNamesAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OperationStatus>("\"Completedd\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReservationStatus>("\"Completed\""));
    }
}
