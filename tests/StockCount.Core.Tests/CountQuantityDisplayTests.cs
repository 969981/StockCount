using System.Globalization;
using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CountQuantityDisplayTests
{
    [Theory]
    [InlineData("600.0000000000000", "PCS", "600 PCS")]
    [InlineData("900.0000000000000", "PCS", "900 PCS")]
    [InlineData("500.1200000000000", "PCS", "500.12 PCS")]
    [InlineData("0.0000000000000", "PCS", "0 PCS")]
    [InlineData("0.0000000000001", "KG", "0.0000000000001 KG")]
    [InlineData("999999999999999.1234567890123", "PCS", "999999999999999.1234567890123 PCS")]
    [InlineData("1.1234567890123456789012345678", "KG", "1.1234567890123456789012345678 KG")]
    [InlineData("100.0000", "", "100")]
    [InlineData("100.5000", null, "100.5")]
    [InlineData("2.500", " SET ", "2.5 SET")]
    public void RemovesOnlyTrailingZerosAndPreservesQuantity(string raw, string? unit, string expected)
    {
        var quantity = decimal.Parse(raw, CultureInfo.InvariantCulture);
        Assert.Equal(expected, CountQuantityDisplay.Format(quantity, unit));
    }
}
