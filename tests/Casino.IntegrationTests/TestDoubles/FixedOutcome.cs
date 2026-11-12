using Casino.Game.Rounds;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class FixedOutcome(decimal payout) : IOutcome
{
    public decimal Draw(decimal stake) => payout;
}
