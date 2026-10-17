namespace HappyInventory.Domain.ValueObjects;

/// <summary>USD amount with exactly two fractional digits, persisted as minor units.</summary>
public static class Money
{
    public const long MaxMinor = 99_999_999_999;

    public static long ToMinor(decimal amount)
    {
        if (amount < 0m || amount > 999_999_999.99m || decimal.Truncate(amount * 100m) != amount * 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        return checked((long)(amount * 100m));
    }

    public static decimal FromMinor(long minor)
    {
        if (minor < 0 || minor > MaxMinor)
        {
            throw new ArgumentOutOfRangeException(nameof(minor));
        }

        return minor / 100m;
    }
}
