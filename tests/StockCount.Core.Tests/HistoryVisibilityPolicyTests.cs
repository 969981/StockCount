using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class HistoryVisibilityPolicyTests
{
    [Fact]
    public void MissingPreferenceDefaultsOff() =>
        Assert.False(HistoryVisibilityPolicy.DefaultEnabled);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesStoredPreference(bool value) =>
        Assert.Equal(value, HistoryVisibilityPolicy.ReadEnabled(() => value));

    [Fact]
    public void ReadFailureDefaultsOff() =>
        Assert.False(HistoryVisibilityPolicy.ReadEnabled(() => throw new InvalidOperationException()));

    [Theory]
    [InlineData(false,true,false,false,false)]
    [InlineData(true,true,false,false,true)]
    [InlineData(true,false,false,false,false)]
    [InlineData(true,true,true,false,false)]
    [InlineData(true,true,false,true,false)]
    public void OnlyEnabledIdleVisiblePageMayNavigate(bool enabled, bool visible, bool busy,
        bool navigating, bool expected) =>
        Assert.Equal(expected, HistoryVisibilityPolicy.CanNavigate(enabled, visible, busy, navigating));
}
