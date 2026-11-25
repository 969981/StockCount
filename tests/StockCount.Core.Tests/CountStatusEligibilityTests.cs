using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CountStatusEligibilityTests
{
    [Theory]
    [InlineData(true, true, true, null)]
    [InlineData(false, true, true, null)]
    [InlineData(true, false, true, "ERP_WRITE_UNAVAILABLE")]
    [InlineData(false, false, true, "PRODUCTION_WRITE_DISABLED")]
    [InlineData(true, true, false, "SCHEMA_NOT_APPROVED")]
    [InlineData(false, true, false, "SCHEMA_NOT_APPROVED")]
    [InlineData(true, false, false, "ERP_WRITE_UNAVAILABLE")]
    [InlineData(false, false, false, "PRODUCTION_WRITE_DISABLED")]
    public void EligibilityDependsOnlyOnEnvironmentAuthorization(
        bool isTest, bool writesEnabled, bool reviewed, string? expected)
    {
        // The eligibility API has no status argument by design.
        Assert.Equal(expected,
            CountWritePolicy.CountSubmissionBlockReason(isTest, writesEnabled, reviewed));
    }
}
