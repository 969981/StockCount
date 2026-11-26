using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class SuccessToastPolicyTests
{
    [Fact]
    public void DefaultMatchesWorkflowFallback()
    {
        Assert.Equal(2000, SuccessToastPolicy.DefaultDurationMs);
        Assert.Equal(2000, CountScanWorkflow.SuccessToastDurationMs);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 3000)]
    [InlineData(4, 5000)]
    public void ConfiguredDurationsRoundTrip(int index, int milliseconds)
    {
        Assert.Equal(milliseconds, SuccessToastPolicy.DurationAt(index));
        Assert.Equal(index, SuccessToastPolicy.IndexOf(milliseconds));
        Assert.Equal(milliseconds, SuccessToastPolicy.Normalize(milliseconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(400)]
    [InlineData(1500)]
    [InlineData(6000)]
    [InlineData(int.MaxValue)]
    public void InvalidStoredDurationUsesDefault(int value)
    {
        Assert.Equal(2000, SuccessToastPolicy.Normalize(value));
        Assert.Equal(2, SuccessToastPolicy.IndexOf(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InvalidIndexUsesDefault(int index) =>
        Assert.Equal(2000, SuccessToastPolicy.DurationAt(index));

    [Fact]
    public void OlderTimerCannotHideNewerToastAndNavigationInvalidates()
    {
        var lifetime = new SuccessToastLifetime();
        Assert.False(lifetime.IsCurrent(0));
        var first = lifetime.Begin();
        Assert.True(lifetime.IsCurrent(first));
        var second = lifetime.Begin();
        Assert.False(lifetime.IsCurrent(first));
        Assert.True(lifetime.IsCurrent(second));
        lifetime.Invalidate();
        Assert.False(lifetime.IsCurrent(second));
        var third = lifetime.Begin();
        Assert.True(lifetime.IsCurrent(third));
        Assert.False(lifetime.IsCurrent(second));
    }
}
