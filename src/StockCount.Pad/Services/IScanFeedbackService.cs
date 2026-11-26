using StockCount.Core;

namespace StockCount.Pad.Services;

public interface IScanFeedbackService
{
    ScanFeedbackOptions Options { get; }
    void UpdateOptions(ScanFeedbackOptions options);
    void Notify(FeedbackEvent feedback);
    void Preview(FeedbackKind kind);
    void Resume();
    void Suspend();
}
