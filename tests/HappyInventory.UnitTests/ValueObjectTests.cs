using HappyInventory.Domain.ValueObjects;

namespace HappyInventory.UnitTests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("0.00", 0L)]
    [InlineData("12.50", 1250L)]
    [InlineData("999999999.99", Money.MaxMinor)]
    public void MoneyRoundTripsAtSupportedBoundaries(string decimalText, long expectedMinor)
    {
        var amount = decimal.Parse(decimalText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedMinor, Money.ToMinor(amount));
        Assert.Equal(amount, Money.FromMinor(expectedMinor));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.001")]
    [InlineData("1000000000.00")]
    public void MoneyRejectsInvalidAmounts(string decimalText)
    {
        var amount = decimal.Parse(decimalText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToMinor(amount));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(Money.MaxMinor + 1)]
    public void MoneyRejectsInvalidMinorUnits(long minor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromMinor(minor));
    }

    [Fact]
    public void NameKeysIgnoreOuterWhitespaceCaseAndCompatibilityForms()
    {
        Assert.Equal(NormalizedText.Key("  Widget  "), NormalizedText.Key("widget"));
        Assert.Equal(NormalizedText.Key("ＦＯＯ"), NormalizedText.Key("foo"));
    }

    [Fact]
    public void NameKeysRejectBlankInput()
    {
        Assert.Throws<ArgumentException>(() => NormalizedText.Key(" \t "));
        Assert.Throws<ArgumentNullException>(() => NormalizedText.Key(null!));
    }
}
