namespace StockCount.Pad.Services;

/// <summary>PDA hardware scanner bridge. UI unsubscribes when page is not visible.</summary>
public static class ScanFeed
{
    public static event Action<string>? ScanReceived;
    public static void OnScan(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) ScanReceived?.Invoke(value);
    }
}
