using Casino.Game.Errors.Exceptions;

namespace Casino.Game.Finance;

public static class Money
{
    public static decimal Validate(decimal amount)
    {
        if (amount < 1m || amount > 10m || decimal.Round(amount, 2) != amount)
            throw new DomainException("Bet must be between $1 and $10 and have at most two decimal places.");
        return amount;
    }

    public static long ToMinorUnits(decimal amount)
    {
        if (decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Fractional cents cannot be stored.");
        return checked((long)(amount * 100m));
    }

    public static void Id(Guid id)
    {
        if (id == Guid.Empty) throw new DomainException("An ID must not be empty.");
    }
}
