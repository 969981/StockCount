using System.Globalization;
using System.Text.RegularExpressions;

namespace StockCount.Server.Hosting;

/// <summary>
/// Localizes only the Avalonia presentation, never the persisted, auditable raw SQLite records.
/// Unknown diagnostics and stack traces deliberately retain their original text.
/// </summary>
public static class LogLocalizer
{
    private static readonly Regex HttpRequest = new(
        @"^HTTP (?<verb>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS) (?<path>\S+) -> (?<status>\d{3}) \((?<duration>\d+) ms\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ServerLog ToChinese(ServerLog raw)
    {
        var time = DateTimeOffset.TryParse(raw.TimestampUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : raw.TimestampUtc;

        var level = raw.Level switch
        {
            "Trace" => "追踪",
            "Debug" => "调试",
            "Information" => "信息",
            "Warning" => "警告",
            "Error" => "错误",
            "Critical" => "严重错误",
            "None" => "无级别",
            _ => raw.Level
        };

        var category = raw.Category switch
        {
            "Microsoft.Hosting.Lifetime" => "服务生命周期",
            "Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServer" => "HTTP 服务器",
            "StockCount.Api.Http" => "接口访问",
            "StockCount.Api.Connection" => "PDA 连接诊断",
            _ => raw.Category.StartsWith("StockCount.", StringComparison.Ordinal)
                ? "盘点服务 · " + raw.Category
                : raw.Category
        };

        var message = LocalizeMessage(raw.Message);
        return raw with
        {
            TimestampUtc = time,
            Level = level,
            Category = category,
            Message = message
        };
    }

    public static string LocalizeMessage(string message)
    {
        if (message.StartsWith("Now listening on: ", StringComparison.Ordinal))
        {
            var url = message["Now listening on: ".Length..];
            return "正在监听：" + url;
        }

        if (message.StartsWith("Hosting environment: ", StringComparison.Ordinal))
        {
            var env = message["Hosting environment: ".Length..];
            var label = env switch
            {
                "Development" => "开发环境",
                "Production" => "正式运行环境",
                "Staging" => "预发布环境",
                _ => env
            };
            return "运行环境：" + label;
        }

        if (message.StartsWith("Content root path: ", StringComparison.Ordinal))
            return "应用工作目录：" + message["Content root path: ".Length..];

        if (message is "Application started. Press Ctrl+C to shut down.")
            return "服务已启动。";

        if (message.StartsWith("Application is shutting down", StringComparison.Ordinal))
            return "服务正在关闭。";

        if (message.StartsWith("The ASP.NET Core developer certificate is not trusted.", StringComparison.Ordinal))
            return "ASP.NET Core 开发 HTTPS 证书未受信任。请检查是否仍使用旧的 HTTPS 调试配置。";

        if (message.StartsWith("Database readiness failed for ", StringComparison.Ordinal))
            return "数据库就绪检查失败，环境：" + message["Database readiness failed for ".Length..];

        var match = HttpRequest.Match(message);
        if (match.Success)
            return $"HTTP 请求：{match.Groups["verb"].Value} {match.Groups["path"].Value}，状态码 {match.Groups["status"].Value}，耗时 {match.Groups["duration"].Value} 毫秒";

        // Keep unknown error details exactly as received; guessing a translation
        // could corrupt evidence during SQL Server or ASP.NET Core diagnostics.
        return message;
    }
}
