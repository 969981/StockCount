using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockCount.Api;
using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class LocationValidationOptionsTests
{
    [Theory]
    [InlineData(false, false, 3, 3, null)]
    [InlineData(false, false, 0, 0, "LOCATION_NOT_FOUND")]
    [InlineData(true, false, 3, 2, null)]
    [InlineData(true, false, 3, 0, "LOCATION_OUTSIDE_WAREHOUSE")]
    [InlineData(false, true, 3, 2, null)]
    [InlineData(false, true, 3, 0, "LOCATION_OUTSIDE_WAREHOUSE")]
    [InlineData(true, true, 3, 1, null)]
    [InlineData(true, true, 3, 0, "LOCATION_OUTSIDE_WAREHOUSE")]
    [InlineData(true, true, 3, 2, "LOCATION_AMBIGUOUS")]
    public void ScopeControlsAcceptance(bool site, bool warehouse, long total, long scoped, string? code)
        => Assert.Equal(code, new LocationScope(site, warehouse).Classify(total, scoped));

    [Fact]
    public void EnvironmentsAreIndependentAndDefaultOff()
    {
        var defaults = new LocationValidationOptions();
        Assert.Equal(new LocationScope(false, false), defaults.For("Test"));
        Assert.Equal(new LocationScope(false, false), defaults.For("Production"));
        var configured = new LocationValidationOptions(true, false, false, true);
        Assert.Equal(new LocationScope(true, false), configured.For("Test"));
        Assert.Equal(new LocationScope(false, true), configured.For("Production"));
        Assert.Throws<ArgumentOutOfRangeException>(() => configured.For("Other"));
        Assert.Contains("库存站点", configured.For("Test").Describe("LOCATION_OUTSIDE_WAREHOUSE"));
        Assert.Contains("仓库", configured.For("Production").Describe("LOCATION_OUTSIDE_WAREHOUSE"));
    }

    [Fact]
    public async Task ApiReadsFourIndependentSettings()
    {
        await using var app = ApiApplication.Build(Array.Empty<string>(), builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StockCount:Location:TestCheckSite"] = "true",
                ["StockCount:Location:ProductionCheckWarehouse"] = "true"
            }));
        var policy = app.Services.GetRequiredService<LocationValidationOptions>();
        Assert.Equal(new LocationScope(true, false), policy.For("Test"));
        Assert.Equal(new LocationScope(false, true), policy.For("Production"));
    }
}
