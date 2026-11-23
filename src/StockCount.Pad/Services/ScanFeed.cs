using StockCount.Core;

namespace StockCount.Pad.Services;

/// <summary>
/// The native receiver forwards decoded data only to the current foreground page.
/// Diagnostic events carry metadata only, never raw enterprise label QR text.
/// </summary>
public static class ScanFeed
{
    public const string AndroidBroadcastAction = "com.service.scanner.data";
    public const string AndroidBarcodeExtra = "ScanCode";

    public sealed record ScanDiagnostic(
        DateTimeOffset ObservedAt, string Kind, int Length);

    public static event Action<string>? ScanReceived;
    public static event Action<ScanDiagnostic>? ScanObserved;
    public static ScanDiagnostic? LatestDiagnostic { get; private set; }

    public static void OnScan(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(normalized)) return;

        var observation = new ScanDiagnostic(DateTimeOffset.Now,
            CountScanWorkflow.LooksLikeBoxQr(normalized) ? "箱码格式" : "待校验库位/其他码",
            normalized.Length);
        LatestDiagnostic = observation;
        ScanObserved?.Invoke(observation);
        ScanReceived?.Invoke(normalized);
    }
}
