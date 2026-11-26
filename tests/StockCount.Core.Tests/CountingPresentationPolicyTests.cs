using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CountingPresentationPolicyTests
{
    [Theory]
    [InlineData(0,false)]
    [InlineData(320,false)]
    [InlineData(339.9,false)]
    [InlineData(340,true)]
    [InlineData(400,true)]
    public void NarrowWidthStacks(double width,bool expected) => Assert.Equal(expected,CountingLayoutPolicy.UseInlineActions(width));

    [Fact]
    public void ScrollIntentExpiresAndReplacementCancelsOldCallbacks()
    {
        var state = new CountingScrollIntent();
        var bottom = state.Begin(CountingScrollTarget.Bottom,1000);
        Assert.True(state.CanApply(bottom,1899));
        Assert.False(state.CanApply(bottom,1900));
        var top = state.Begin(CountingScrollTarget.Top,2000);
        Assert.False(state.CanApply(bottom,2001));
        Assert.Equal(CountingScrollTarget.Top,state.Target);
        Assert.True(state.CanApply(top,2001));
        state.Cancel();
        Assert.False(state.CanApply(top,2002));
    }
}
