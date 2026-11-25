using System.Text.RegularExpressions;
using StockCount.Core;

namespace StockCount.Pad.Services;

/// <summary>
/// In-process foreground scan bridge. Android scanner broadcast Action/Extra
/// are device-specific, so operators can configure them without rebuilding.
/// Never retain or expose decoded QR bodies in scanner diagnostics.
/// </summary>
public static class ScanFeed
{
    private const string DefaultAction = "com.service.scanner.data";
    private const string DefaultExtra = "ScanCode";
    private const string ActionKey = "StockCount.Scanner.BroadcastAction";
    private const string ExtraKey = "StockCount.Scanner.BarcodeExtra";

    public static string AndroidBroadcastAction =>
        Preferences.Default.Get(ActionKey, DefaultAction);
    public static string AndroidBarcodeExtra =>
        Preferences.Default.Get(ExtraKey, DefaultExtra);

    public sealed record ScanDiagnostic(DateTimeOffset ObservedAt, string Kind, int Length, string Shape);

    public static event Action<string>? ScanReceived;
    public static event Action<ScanDiagnostic>? ScanObserved;
    public static event Action? ScannerConfigurationChanged;
    public static ScanDiagnostic? LatestDiagnostic { get; private set; }

    public static void Configure(string action, string extra)
    {
        action = action.Trim();
        extra = extra.Trim();
        // Restrict to Android intent-action and extra-key character conventions.
        // No whitespace, untrusted control characters or huge strings.
        if (action.Length is < 3 or > 120 ||
            !Regex.IsMatch(action, @"\A[A-Za-z0-9_.]+\z") ||
            !action.Contains('.'))
            throw new ArgumentException("扫码广播 Action 无效，请输入类似 com.service.scanner.data 的值。");
        if (extra.Length is < 1 or > 80 ||
            !Regex.IsMatch(extra, @"\A[A-Za-z0-9_.]+\z"))
            throw new ArgumentException("扫码内容字段名无效，请输入类似 ScanCode 的值。");

        Preferences.Default.Set(ActionKey, action);
        Preferences.Default.Set(ExtraKey, extra);
        LatestDiagnostic = null;
        ScannerConfigurationChanged?.Invoke();
    }

    private static string DescribeInvisible(string value)
    {
        var positions = value.Select((ch, index) => (ch, index))
            .Where(x => char.IsControl(x.ch) ||
                char.GetUnicodeCategory(x.ch) == System.Globalization.UnicodeCategory.Format)
            .Take(12)
            .Select(x => $"{x.index}:U+{(int)x.ch:X4}");
        var result = string.Join(", ", positions);
        return result.Length == 0 ? "无" : result;
    }

    public static void OnScan(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(normalized)) return;
        var diagnostic = new ScanDiagnostic(DateTimeOffset.Now,
            CountScanWorkflow.LooksLikeBoxQr(normalized) ? "待查询箱码" : "待校验库位/其他码",
            normalized.Length,
            $"原始长度 {value.Length}；ID. 开头：{(normalized.StartsWith("ID.", StringComparison.OrdinalIgnoreCase) ? "是" : "否")}；" +
            $"@@ 分隔符：{Regex.Matches(normalized, "@@").Count}；" +
            $"控制/不可见字符：{DescribeInvisible(normalized)}；" +
            $"首字符 U+{(int)normalized[0]:X4} / 尾字符 U+{(int)normalized[^1]:X4}");
        LatestDiagnostic = diagnostic;
        ScanObserved?.Invoke(diagnostic);
        ScanReceived?.Invoke(normalized);
    }
}
