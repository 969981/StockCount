namespace StockCount.Core;

/// <summary>Only routine, non-ambiguous errors auto-dismiss; never changes domain state.</summary>
public static class BusinessToastPolicy
{
    public static bool IsAutomaticFailure(CountScanFeedback feedback, bool submission, bool pending)
    {
        if (pending) return false;
        if (!submission) return feedback.Kind is "Error" or "Duplicate";
        return feedback.Kind == "Error" && feedback.ErrorCode == "BOX_ALREADY_RECORDED";
    }
}

public static class FeedbackAudioPolicy
{
    public static FeedbackKind SampleKind(FeedbackKind kind) =>
        kind == FeedbackKind.WarningUnknown ? FeedbackKind.Failure : kind;

    public static bool CanUseFallback(FeedbackKind kind) =>
        kind is FeedbackKind.Success or FeedbackKind.Failure or FeedbackKind.WarningUnknown;

    public static bool MayInterrupt(FeedbackKind next, FeedbackKind active, long now, long until) =>
        next != FeedbackKind.Read || active is FeedbackKind.Read or FeedbackKind.None || now >= until;
}
