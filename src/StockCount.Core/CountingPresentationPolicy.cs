namespace StockCount.Core;

public static class CountingLayoutPolicy
{
    public const double InlineMinimumWidth = 340;
    public static bool UseInlineActions(double width) => double.IsFinite(width) && width >= InlineMinimumWidth;
}

public enum CountingScrollTarget { None, Top, Bottom }

/// <summary>Short-lived UI-only intent; expired or retired callbacks cannot move a new draft.</summary>
public sealed class CountingScrollIntent
{
    public const long LifetimeMs = 900;
    public long Version { get; private set; }
    public CountingScrollTarget Target { get; private set; }
    private long _until;
    public long Begin(CountingScrollTarget target, long nowMs)
    {
        Target = target;
        _until = nowMs + LifetimeMs;
        return ++Version;
    }
    public void Cancel()
    {
        Target = CountingScrollTarget.None;
        ++Version;
    }
    public bool CanApply(long version, long nowMs) =>
        version == Version && Target != CountingScrollTarget.None && nowMs < _until;
}
