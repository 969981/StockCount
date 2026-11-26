using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class AudioGainPolicyTests
{
    [Theory]
    [InlineData(-1,4)]
    [InlineData(2,2)]
    [InlineData(3,3)]
    [InlineData(4,4)]
    [InlineData(5,5)]
    [InlineData(12,12)]
    [InlineData(13,4)]
    public void StoredGainValidation(int raw,int expected) => Assert.Equal(expected,AudioGainPolicy.Normalize(raw));

    [Theory]
    [InlineData(1.9,2)]
    [InlineData(4.49,4)]
    [InlineData(4.5,5)]
    [InlineData(11.6,12)]
    [InlineData(99.0,12)]
    public void SliderSnapsToQuarterSteps(double value,int expected) => Assert.Equal(expected,AudioGainPolicy.FromSlider(value));

    [Theory]
    [InlineData(FeedbackKind.Success,2,4,0.5)]
    [InlineData(FeedbackKind.Failure,3,4,0.75)]
    [InlineData(FeedbackKind.Success,4,4,1.0)]
    [InlineData(FeedbackKind.Failure,5,5,1.0)]
    [InlineData(FeedbackKind.WarningUnknown,12,12,1.0)]
    [InlineData(FeedbackKind.Read,2,4,1.0)]
    [InlineData(FeedbackKind.Read,12,4,1.0)]
    public void SelectsSameAssetAndLegalVolume(FeedbackKind kind,int step,int asset,double volume)
    {
        Assert.Equal(asset,AudioGainPolicy.AssetStep(kind,step));
        Assert.Equal((float)volume,AudioGainPolicy.PlaybackVolume(kind,step));
    }

    [Fact]
    public void DefaultPreservesOriginalAndSwitches()
    {
        var options = new ScanFeedbackOptions();
        Assert.Equal(4,options.GainQuarterSteps);
        Assert.Equal(1.0,AudioGainPolicy.Multiplier(options.GainQuarterSteps));
        Assert.False((options with { SoundEnabled = false, GainQuarterSteps = 12 }).AllowsSound(FeedbackKind.Failure));
        Assert.True((options with { SoundEnabled = false }).AllowsVibration(FeedbackKind.Failure));
    }
}
