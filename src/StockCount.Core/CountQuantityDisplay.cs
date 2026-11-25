using System.Globalization;

namespace StockCount.Core;

/// <summary>Display-only decimal formatting; never rounds or changes persisted counts.</summary>
public static class CountQuantityDisplay
{
    public static string Format(decimal quantity, string? unit)
    {
        // Decimal supports at most 28 fractional digits. Optional digit placeholders
        // remove trailing zeros without using scientific notation or losing precision.
        var number = quantity.ToString("0.############################", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : number + " " + unit.Trim();
    }
}
