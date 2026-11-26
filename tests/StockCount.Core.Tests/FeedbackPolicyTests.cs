using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class FeedbackPolicyTests
{
    [Theory]
    [InlineData(FeedbackReason.ReadAccepted, FeedbackKind.Read)]
    [InlineData(FeedbackReason.LocationUpdated, FeedbackKind.Success)]
    [InlineData(FeedbackReason.BoxResolved, FeedbackKind.Success)]
    [InlineData(FeedbackReason.ScanError, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.Duplicate, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.ScanException, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.QueueFull, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.Busy, FeedbackKind.None)]
    [InlineData(FeedbackReason.SameDraft, FeedbackKind.None)]
    [InlineData(FeedbackReason.DraftConfirmation, FeedbackKind.None)]
    [InlineData(FeedbackReason.PendingBlocked, FeedbackKind.WarningUnknown)]
    [InlineData(FeedbackReason.InactiveOrDiagnostic, FeedbackKind.None)]
    [InlineData(FeedbackReason.QuantityInvalid, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.SubmitRejected, FeedbackKind.Failure)]
    [InlineData(FeedbackReason.SubmitUnknown, FeedbackKind.WarningUnknown)]
    [InlineData(FeedbackReason.Submitted, FeedbackKind.Success)]
    [InlineData(FeedbackReason.RecoveredSubmitted, FeedbackKind.Success)]
    [InlineData(FeedbackReason.ConfirmedCleanupFailed, FeedbackKind.WarningUnknown)]
    [InlineData(FeedbackReason.Ready, FeedbackKind.None)]
    [InlineData(FeedbackReason.SubmissionException, FeedbackKind.WarningUnknown)]
    [InlineData(FeedbackReason.NonBusiness, FeedbackKind.None)]
    [InlineData(FeedbackReason.Preview, FeedbackKind.None)]
    public void TriggerMatrix(FeedbackReason reason, FeedbackKind expected) =>
        Assert.Equal(expected, new FeedbackEvent(reason).Kind);

    [Theory]
    [InlineData("LocationUpdated", false, false, FeedbackReason.LocationUpdated)]
    [InlineData("BoxResolved", false, false, FeedbackReason.BoxResolved)]
    [InlineData("Duplicate", false, false, FeedbackReason.Duplicate)]
    [InlineData("Error", false, false, FeedbackReason.ScanError)]
    [InlineData("Error", true, false, FeedbackReason.SubmitRejected)]
    [InlineData("Unknown", true, false, FeedbackReason.SubmitUnknown)]
    [InlineData("Submitted", true, false, FeedbackReason.Submitted)]
    [InlineData("Submitted", true, true, FeedbackReason.RecoveredSubmitted)]
    [InlineData("Busy", true, false, FeedbackReason.Busy)]
    [InlineData("Ready", true, true, FeedbackReason.Ready)]
    [InlineData("DraftNeedsConfirmation", false, false, FeedbackReason.DraftConfirmation)]
    [InlineData("unrecognized", false, false, FeedbackReason.NonBusiness)]
    public void MapsWorkflow(string kind, bool submission, bool recovery, FeedbackReason expected) =>
        Assert.Equal(expected, FeedbackPolicy.FromWorkflow(kind, submission, recovery));

    [Theory]
    [InlineData(FeedbackKind.None, 0, 0)]
    [InlineData(FeedbackKind.Read, 125, 0)]
    [InlineData(FeedbackKind.Success, 626, 200)]
    [InlineData(FeedbackKind.Failure, 840, 1000)]
    [InlineData(FeedbackKind.WarningUnknown, 840, 1000)]
    public void Durations(FeedbackKind kind, int audio, int vibration)
    {
        Assert.Equal(audio, FeedbackPolicy.AudioMilliseconds(kind));
        Assert.Equal(vibration, FeedbackPolicy.VibrationMilliseconds(kind));
    }

    [Fact]
    public void DefaultsAndIndependentChannels()
    {
        var options = new ScanFeedbackOptions();
        Assert.True(options.AllowsSound(FeedbackKind.Success));
        Assert.True(options.AllowsVibration(FeedbackKind.Failure));
        Assert.False(options.AllowsSound(FeedbackKind.Read));
        Assert.True((options with { ReadEnabled = true }).AllowsSound(FeedbackKind.Read));
        Assert.False((options with { ReadEnabled = true }).AllowsVibration(FeedbackKind.Read));
        Assert.True((options with { SoundEnabled = false }).AllowsVibration(FeedbackKind.Success));
        Assert.True((options with { VibrationEnabled = false }).AllowsSound(FeedbackKind.Failure));
        Assert.False((options with { FailureEnabled = false }).AllowsSound(FeedbackKind.WarningUnknown));
        Assert.False((options with { SuccessEnabled = false }).AllowsVibration(FeedbackKind.Success));
    }

    [Fact]
    public void UnknownDoesNotConsumeSuccessAndUuidIsDeduplicated()
    {
        var gate = new FeedbackGate();
        var id = Guid.NewGuid();
        Assert.True(gate.Accept(new(FeedbackReason.SubmitUnknown, id), 0));
        Assert.True(gate.Accept(new(FeedbackReason.ConfirmedCleanupFailed, id), 5));
        Assert.True(gate.Accept(new(FeedbackReason.RecoveredSubmitted, id), 10));
        Assert.False(gate.Accept(new(FeedbackReason.Submitted, id), 20));
        Assert.True(gate.Accept(new(FeedbackReason.Submitted, Guid.NewGuid()), 21));
    }

    [Fact]
    public void ThrottlesWarningStormButNotOrdinaryResults()
    {
        var gate = new FeedbackGate();
        Assert.True(gate.Accept(new(FeedbackReason.QueueFull), 0));
        Assert.False(gate.Accept(new(FeedbackReason.PendingBlocked), 100));
        Assert.True(gate.Accept(new(FeedbackReason.QueueFull), 750));
        Assert.True(gate.Accept(new(FeedbackReason.Duplicate), 751));
        Assert.True(gate.Accept(new(FeedbackReason.BoxResolved), 752));
        Assert.True(gate.Accept(new(FeedbackReason.BoxResolved), 753));
        Assert.False(gate.Accept(new(FeedbackReason.Busy), 754));
    }
}
