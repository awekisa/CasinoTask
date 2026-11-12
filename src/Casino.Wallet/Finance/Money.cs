using Casino.Wallet.Errors.Exceptions;

namespace Casino.Wallet.Finance;

public static class Money
{
    public const decimal MaxBalance = 10_000_000_000m;

    public static decimal Validate(decimal amount, bool bet = false)
    {
        if (amount <= 0 || amount > MaxBalance || decimal.Round(amount, 2) != amount)
            throw new DomainException("Amount must be positive, within the limit, and have at most two decimal places.");
        if (bet && (amount < 1m || amount > 10m)) throw new DomainException("Bet must be between $1 and $10.");
        return amount;
    }

    public static long ToMinorUnits(decimal amount)
    {
        if (decimal.Round(amount, 2) != amount) throw new ArgumentOutOfRangeException(nameof(amount), "Fractional cents cannot be stored.");
        return checked((long)(amount * 100m));
    }

    public static void Id(Guid id) { if (id == Guid.Empty) throw new DomainException("An ID must not be empty."); }
}
