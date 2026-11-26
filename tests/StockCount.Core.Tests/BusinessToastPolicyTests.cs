using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class BusinessToastPolicyTests
{
    [Theory]
    [InlineData("Error", false, false, null, true)]
    [InlineData("Duplicate", false, false, null, true)]
    [InlineData("Error", false, true, null, false)]
    [InlineData("Duplicate", false, true, null, false)]
    [InlineData("Error", true, false, "BOX_ALREADY_RECORDED", true)]
    [InlineData("Error", true, true, "BOX_ALREADY_RECORDED", false)]
    [InlineData("Error", true, false, null, false)]
    [InlineData("Error", true, false, "ERP_SCHEMA_UNAVAILABLE", false)]
    [InlineData("Unknown", false, true, null, false)]
    [InlineData("Unknown", true, true, null, false)]
    [InlineData("DraftNeedsConfirmation", false, false, null, false)]
    [InlineData("Busy", false, false, null, false)]
    [InlineData("Submitted", true, false, null, false)]
    [InlineData("BoxResolved", false, false, null, false)]
    public void RoutineOnly(string kind, bool submit, bool pending, string? code, bool expected)
    {
        var feedback = new CountScanFeedback(kind, "synthetic") { ErrorCode = code };
        Assert.Equal(expected, BusinessToastPolicy.IsAutomaticFailure(feedback, submit, pending));
    }

    [Theory]
    [InlineData(FeedbackKind.Success, FeedbackKind.Success, true)]
    [InlineData(FeedbackKind.Failure, FeedbackKind.Failure, true)]
    [InlineData(FeedbackKind.WarningUnknown, FeedbackKind.Failure, true)]
    [InlineData(FeedbackKind.Read, FeedbackKind.Read, false)]
    [InlineData(FeedbackKind.None, FeedbackKind.None, false)]
    public void FallbackRoutesBusinessAudio(FeedbackKind kind, FeedbackKind sample, bool fallback)
    {
        Assert.Equal(sample, FeedbackAudioPolicy.SampleKind(kind));
        Assert.Equal(fallback, FeedbackAudioPolicy.CanUseFallback(kind));
    }

    [Theory]
    [InlineData(FeedbackKind.Read, FeedbackKind.Failure, 100, 840, false)]
    [InlineData(FeedbackKind.Read, FeedbackKind.WarningUnknown, 100, 840, false)]
    [InlineData(FeedbackKind.Read, FeedbackKind.Success, 626, 626, true)]
    [InlineData(FeedbackKind.Read, FeedbackKind.Read, 10, 125, true)]
    [InlineData(FeedbackKind.Failure, FeedbackKind.Success, 10, 626, true)]
    [InlineData(FeedbackKind.Success, FeedbackKind.Failure, 10, 840, true)]
    public void OptionalReadNeverInterruptsBusinessAudio(FeedbackKind next, FeedbackKind active,
        long now, long until, bool expected) =>
        Assert.Equal(expected, FeedbackAudioPolicy.MayInterrupt(next, active, now, until));
}
