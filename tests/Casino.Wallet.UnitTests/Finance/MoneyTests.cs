using System.Globalization;
using Casino.Wallet.Errors.Exceptions;
using Casino.Wallet.Finance;
using Xunit;

namespace Casino.Wallet.UnitTests.Finance;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0.99")]
    [InlineData("10.01")]
    [InlineData("1.005")]
    [InlineData("-1")]
    [InlineData("0")]
    public void InvalidBetsAreRejected(string input)
    {
        var amount = decimal.Parse(input, CultureInfo.InvariantCulture);
        Assert.Throws<DomainException>(() => Money.Validate(amount, true));
    }
}
