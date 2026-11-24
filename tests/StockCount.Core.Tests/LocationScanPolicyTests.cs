using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class LocationScanPolicyTests
{
    [Theory]
    [InlineData("LOC-DEMO01", true)]
    [InlineData("001", true)]
    [InlineData("A-01", true)]
    [InlineData("01234567890", false)]
    [InlineData("A\tB", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void OnlyEnforcesKnownFormatConstraints(string code, bool valid) =>
        Assert.Equal(valid, LocationScanPolicy.IsValidFormat(code));

    [Fact]
    public void RemovesScannerLineTerminatorsWithoutRemovingEmbeddedControlCharacters()
    {
        Assert.Equal("LOC-01", LocationScanPolicy.Normalize(" LOC-01 \r\n"));
        Assert.False(LocationScanPolicy.IsValidFormat(LocationScanPolicy.Normalize("LOC\t01")));
    }

    [Theory]
    [InlineData(0,0,"LOCATION_NOT_FOUND")]
    [InlineData(2,0,"LOCATION_OUTSIDE_WAREHOUSE")]
    [InlineData(1,1,null)]
    [InlineData(3,1,null)]
    [InlineData(3,2,"LOCATION_AMBIGUOUS")]
    public void DistinguishesDatabaseLookupOutcomes(long total, long scoped, string? code) =>
        Assert.Equal(code, LocationScanPolicy.ClassifyLookup(total, scoped));
}
