using Casino.Game.Rounds.Implementation;
using Xunit;

namespace Casino.Game.UnitTests.Rounds;

public sealed class RandomOutcomeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void OutcomeBoundaries(decimal stake)
    {
        foreach (var category in Enumerable.Range(0, 100))
        foreach (var sample in new[] { 0, (int)((category < 90 ? stake * 2 : stake * 8) * 100m) - 1 })
        {
            var payout = RandomOutcome.Calculate(stake, category, sample);
            if (category < 50) Assert.Equal(0, payout);
            else if (category < 90) Assert.InRange(payout, 0.01m, stake * 2);
            else Assert.InRange(payout, stake * 2 + 0.01m, stake * 10);
        }
        Assert.Equal(stake * 10, RandomOutcome.Calculate(stake, 99, (int)(stake * 8 * 100m) - 1));
    }
}
