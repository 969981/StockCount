using StockCount.Core;
using StockCount.Infrastructure;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class EnvironmentWritePermissionsTests
{
    private static readonly SelectedEnvironment Test = new("Test", "XTEST");
    private static readonly SelectedEnvironment Production = new("Production", "COB");

    [Fact]
    public void BothEnvironmentsCanBeEnabledIndependently()
    {
        var settings = new CountWriteOptions(
            EnableTestWrites: true,
            EnableProductionWrites: true,
            AuditUser: "PDA",
            TestSchemaReviewed: true,
            ProductionSchemaReviewed: true);

        Assert.True(settings.IsEnabled(Test));
        Assert.True(settings.IsEnabled(Production));
        Assert.True(settings.IsReviewed(Test));
        Assert.True(settings.IsReviewed(Production));
    }

    [Fact]
    public void ProductionToggleNeverImplicitlyEnablesTestWrites()
    {
        var settings = new CountWriteOptions(false, true, "PDA", false, true);
        Assert.False(settings.IsEnabled(Test));
        Assert.True(settings.IsEnabled(Production));
        Assert.False(settings.IsReviewed(Test));
        Assert.True(settings.IsReviewed(Production));
    }

    [Fact]
    public void TestToggleNeverImplicitlyEnablesProductionWrites()
    {
        var settings = new CountWriteOptions(true, false, "PDA", true, false);
        Assert.True(settings.IsEnabled(Test));
        Assert.False(settings.IsEnabled(Production));
        Assert.True(settings.IsReviewed(Test));
        Assert.False(settings.IsReviewed(Production));
    }

    [Fact]
    public void ProductionWriteNeedsIndependentSchemaReview()
    {
        var settings = new CountWriteOptions(true, true, "PDA", true, false);
        Assert.True(settings.IsEnabled(Production));
        Assert.False(settings.IsReviewed(Production));
        Assert.True(settings.IsReviewed(Test));
    }

    [Fact]
    public void BothEnvironmentsAreFailClosedByDefault()
    {
        var settings = new CountWriteOptions(false, false, "", false, false);
        Assert.False(settings.IsEnabled(Production));
        Assert.False(settings.IsEnabled(Test));
    }

    [Fact]
    public void UntrustedSchemaCannotBeInterpolatedIntoInsert()
    {
        Assert.Equal("[COB]", EnvironmentSelector.TablePrefix(Production));
        Assert.Equal("[XTEST]", EnvironmentSelector.TablePrefix(Test));
        Assert.Throws<InvalidOperationException>(() => EnvironmentSelector.TablePrefix(
            new SelectedEnvironment("Other", "dbo;DROP TABLE XCOUTD")));
    }
}
