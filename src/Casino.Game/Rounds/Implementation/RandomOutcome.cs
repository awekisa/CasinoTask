using Casino.Game.Finance;
using System.Security.Cryptography;

namespace Casino.Game.Rounds.Implementation;

public sealed class RandomOutcome : IOutcome
{
    public decimal Draw(decimal stake)
    {
        Money.Validate(stake);
        var category = RandomNumberGenerator.GetInt32(100);
        var centCount = (int)(Money.ToMinorUnits(stake) * (category < 90 ? 2 : 8));
        return Calculate(stake, category, RandomNumberGenerator.GetInt32(centCount));
    }

    public static decimal Calculate(decimal stake, int category, int sample)
    {
        Money.Validate(stake);

        if (category < 0 || category >= 100)
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        // Categories 50–89 pay between one cent and twice the stake.
        var minimumPayout = 0.01m;
        var maximumPayout = stake * 2m;

        // Categories 90–99 pay above twice the stake, up to ten times the stake.
        if (category >= 90)
        {
            minimumPayout = stake * 2m + 0.01m;
            maximumPayout = stake * 10m;
        }

        // Both endpoints are included. For example, $0.01–$4.00 has 400 possible payouts.
        var possiblePayoutCount = Money.ToMinorUnits(maximumPayout - minimumPayout) + 1;
        if (sample < 0 || sample >= possiblePayoutCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }

        // Categories 0–49 lose, regardless of the valid sample.
        if (category < 50)
        {
            return 0m;
        }

        var payoutOffsetInDollars = sample / 100m;
        return minimumPayout + payoutOffsetInDollars;
    }
}
