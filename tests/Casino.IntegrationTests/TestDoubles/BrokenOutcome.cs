using Casino.Game.Rounds;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class BrokenOutcome : IOutcome
{
    public decimal Draw(decimal stake) => throw new InvalidOperationException("Crash before outcome persisted");
}
