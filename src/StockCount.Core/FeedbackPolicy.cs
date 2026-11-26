namespace StockCount.Core;

/// <summary>Presentation-only feedback; never determines transaction outcome.</summary>
public enum FeedbackKind { None, Read, Success, Failure, WarningUnknown }

public enum FeedbackReason
{
    ReadAccepted, LocationUpdated, BoxResolved, ScanError, Duplicate, ScanException,
    QueueFull, Busy, SameDraft, DraftConfirmation, PendingBlocked, InactiveOrDiagnostic,
    QuantityInvalid, SubmitRejected, SubmitUnknown, Submitted, RecoveredSubmitted,
    ConfirmedCleanupFailed, Ready, SubmissionException, NonBusiness, Preview
}

public sealed record FeedbackEvent(FeedbackReason Reason, Guid? RequestId = null)
{
    public FeedbackKind Kind => FeedbackPolicy.Classify(Reason);
}

public sealed record ScanFeedbackOptions(bool SoundEnabled = true, bool VibrationEnabled = true,
    bool SuccessEnabled = true, bool FailureEnabled = true, bool ReadEnabled = false,
    int GainQuarterSteps = AudioGainPolicy.DefaultStep)
{
    public bool AllowsSound(FeedbackKind kind) => SoundEnabled && CategoryEnabled(kind);
    public bool AllowsVibration(FeedbackKind kind) =>
        VibrationEnabled && kind != FeedbackKind.Read && CategoryEnabled(kind);

    private bool CategoryEnabled(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => SuccessEnabled,
        FeedbackKind.Failure or FeedbackKind.WarningUnknown => FailureEnabled,
        FeedbackKind.Read => ReadEnabled,
        _ => false
    };
}

public static class FeedbackPolicy
{
    public static FeedbackKind Classify(FeedbackReason reason) => reason switch
    {
        FeedbackReason.ReadAccepted => FeedbackKind.Read,
        FeedbackReason.LocationUpdated or FeedbackReason.BoxResolved or
            FeedbackReason.Submitted or FeedbackReason.RecoveredSubmitted => FeedbackKind.Success,
        FeedbackReason.ScanError or FeedbackReason.Duplicate or FeedbackReason.ScanException or
            FeedbackReason.QueueFull or FeedbackReason.QuantityInvalid or
            FeedbackReason.SubmitRejected => FeedbackKind.Failure,
        FeedbackReason.PendingBlocked or FeedbackReason.SubmitUnknown or
            FeedbackReason.ConfirmedCleanupFailed or FeedbackReason.SubmissionException => FeedbackKind.WarningUnknown,
        _ => FeedbackKind.None
    };

    public static FeedbackReason FromWorkflow(string kind, bool submission = false, bool recovery = false) =>
        kind switch
        {
            "LocationUpdated" => FeedbackReason.LocationUpdated,
            "BoxResolved" => FeedbackReason.BoxResolved,
            "Duplicate" => FeedbackReason.Duplicate,
            "Error" => submission ? FeedbackReason.SubmitRejected : FeedbackReason.ScanError,
            "Unknown" => FeedbackReason.SubmitUnknown,
            "Submitted" => recovery ? FeedbackReason.RecoveredSubmitted : FeedbackReason.Submitted,
            "Busy" => FeedbackReason.Busy,
            "DraftNeedsConfirmation" => FeedbackReason.DraftConfirmation,
            "Ready" => FeedbackReason.Ready,
            _ => FeedbackReason.NonBusiness
        };

    public static int VibrationMilliseconds(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => 200,
        FeedbackKind.Failure or FeedbackKind.WarningUnknown => 1000,
        _ => 0
    };

    public static int AudioMilliseconds(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Read => 125,
        FeedbackKind.Success => 626,
        FeedbackKind.Failure or FeedbackKind.WarningUnknown => 840,
        _ => 0
    };
}

/// <summary>Single foreground consumer; elapsed time must be monotonic.</summary>
public sealed class FeedbackGate
{
    private readonly HashSet<Guid> _confirmed = new();
    private long? _lastWarning;
    public const int WarningIntervalMs = 750;

    public bool Accept(FeedbackEvent feedback, long nowMs)
    {
        if (feedback.Kind == FeedbackKind.None) return false;
        if ((feedback.Reason is FeedbackReason.Submitted or FeedbackReason.RecoveredSubmitted)
            && feedback.RequestId is Guid id)
            return _confirmed.Add(id);
        // Repeated rejection of scans during a pending operation or a full queue
        // is noisy. Do not throttle distinct ordinary validation results.
        if (feedback.Reason is FeedbackReason.QueueFull or FeedbackReason.PendingBlocked
            or FeedbackReason.SubmitUnknown or FeedbackReason.SubmissionException)
        {
            if (_lastWarning is long last && nowMs - last < WarningIntervalMs) return false;
            _lastWarning = nowMs;
        }
        return true;
    }
}
