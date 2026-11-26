namespace StockCount.Core;

/// <summary>Local UI feature policy, not server authorization.</summary>
public static class HistoryVisibilityPolicy
{
    public const bool DefaultEnabled = false;

    public static bool ReadEnabled(Func<bool> read)
    {
        try { return read(); }
        catch { return DefaultEnabled; }
    }

    public static bool CanNavigate(bool enabled, bool pageVisible, bool busy, bool navigating) =>
        enabled && pageVisible && !busy && !navigating;
}
