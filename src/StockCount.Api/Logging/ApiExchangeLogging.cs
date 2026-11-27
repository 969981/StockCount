using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace StockCount.Api.Logging;

/// <summary>Bounded, read-through / write-through capture. Never buffers entire responses
/// or alters the bytes delivered to the client. Only recognized business JSON fields are logged.</summary>
public sealed class ApiExchangeLogging(RequestDelegate next, ILogger<ApiExchangeLogging> logger)
{
    private const int CaptureLimit = 8192;
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly HashSet<string> BusinessFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "environment", "countNumber", "description", "siteCode", "warehouseCode", "countDate",
        "statusCode", "countType", "canSubmit", "cannotSubmitReason", "items", "nextCursor",
        "count", "rawCode", "locationCode", "isValid", "message", "rawQrCode", "boxNumber",
        "itemCode", "itemName", "specification", "lotNumber", "stockUnit", "labelQty",
        "labelStatusCode", "alreadyRecordedInCount", "detailId", "packageUnit", "quantityPcu",
        "quantityStu", "createdAt", "countedLocation", "countedQtyStu", "savedQtyStu",
        "savedQtyPcu", "requestId", "status", "replayed", "service", "version", "utc",
        "database", "code", "title", "type", "pageSize", "cursor", "search", "operation", "mode", "decision",
        "existing", "updatedAt", "lastCorrectionOldQuantity", "lastCorrectionNewQuantity", "lastCorrectionAtUtc"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var health = path == "/health";
        var remote = context.Connection.RemoteIpAddress;
        if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
        var marked = context.Request.Headers["X-StockCount-Connection-Test"].ToString() == "1";
        if (!(path.StartsWithSegments("/api/v1") || path == "/ready" ||
            (health && (marked || (remote is not null && !IPAddress.IsLoopback(remote))))))
        {
            await next(context);
            return;
        }

        var originalRequest = context.Request.Body;
        var originalResponse = context.Response.Body;
        using var request = new CaptureStream(originalRequest);
        using var response = new CaptureStream(originalResponse);
        var watch = Stopwatch.StartNew();
        var trace = context.TraceIdentifier;
        var environment = context.Request.Headers["X-StockCount-Environment"].ToString() switch
        {
            "Test" => "XTEST", "Production" => "COB", _ => "未指定"
        };
        var diagnostic = Guid.TryParse(context.Request.Headers["X-StockCount-Diagnostic-Id"].ToString(), out var d)
            ? d.ToString("D") : "-";
        var idempotency = Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var id)
            ? id.ToString("D") : "-";
        var failed = false;
        try
        {
            context.Request.Body = request;
            context.Response.Body = response;
            await next(context);
        }
        catch { failed = true; throw; }
        finally
        {
            context.Request.Body = originalRequest;
            context.Response.Body = originalResponse;
            watch.Stop();
            try
            {
                var status = failed && !context.Response.HasStarted ? 500 : context.Response.StatusCode;
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(未匹配路由)";
                var parameters = context.Request.RouteValues
                    .Where(x => x.Key is "countNumber" or "requestId")
                    .ToDictionary(x => x.Key, x => Limit(x.Value?.ToString() ?? "", 200));
                var query = context.Request.Query
                    .Where(x => x.Key is "pageSize" or "cursor" or "search" or "countNumber")
                    .ToDictionary(x => x.Key, x => Limit(x.Value.ToString(), 200));
                logger.Log(status >= 500 || failed ? LogLevel.Error :
                    status >= 400 ? LogLevel.Warning : LogLevel.Information,
                    "API 请求明细 | {Method} {Route} | HTTP {Status} | {Duration} ms\n" +
                    "来源 IP：{IP}:{RemotePort} | 本机端口：{LocalPort} | 帐套：{Environment}\n" +
                    "请求编号：{Trace} | 诊断编号：{Diagnostic} | 幂等键：{Idempotency}\n" +
                    "路径参数：{RouteValues}\n查询参数：{Query}\n入参 JSON：{Request}\n出参 JSON：{Response}\n传输：请求读取 {RequestBytes} B，响应写入 {ResponseBytes} B，{Outcome}",
                    context.Request.Method, route, status, watch.ElapsedMilliseconds,
                    remote?.ToString() ?? "未知", context.Connection.RemotePort, context.Connection.LocalPort,
                    environment, trace, diagnostic, idempotency,
                    JsonSerializer.Serialize(parameters, Json), JsonSerializer.Serialize(query, Json),
                    FormatBody(request, context.Request.ContentType),
                    FormatBody(response, context.Response.ContentType),
                    request.Total, response.Total, failed ? "处理异常或传输中断" : "处理结束");
            }
            catch (Exception)
            {
                // Logging must never change the business result.
            }
        }
    }

    private static string Limit(string value, int max)
    {
        // Defense in depth for credential-like assignments embedded inside otherwise allowed strings.
        var safe = Regex.Replace(value,
            @"(?i)\b(password|pwd|device[-_]?key|access[-_]?key|api[-_]?key|authorization|token|secret)\s*[:=]\s*[^;\s,]+",
            "$1=[REDACTED]");
        return safe.Length <= max ? safe : safe[..max] + "…（已截断）";
    }

    private static string FormatBody(CaptureStream capture, string? contentType)
    {
        if (capture.Total == 0) return "（无正文或未读取）";
        if (capture.Total > CaptureLimit) return $"（正文超过 {CaptureLimit} 字节，已省略；总计 {capture.Total} 字节）";
        if (contentType is null || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return "（非 JSON 正文，不记录）";
        try
        {
            using var doc = JsonDocument.Parse(capture.Bytes);
            var result = JsonSerializer.Serialize(Filter(doc.RootElement), Json);
            return result.Length <= 6000 ? result : "（脱敏后的 JSON 超过 6000 字符，已省略）";
        }
        catch (JsonException) { return "（无效或不完整 JSON，不记录原文）"; }
    }

    private static object? Filter(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Where(p => BusinessFields.Contains(p.Name))
            .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => Filter(g.Last().Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(Filter).ToArray(),
        JsonValueKind.String => Limit(element.GetString() ?? "", 300),
        JsonValueKind.Number => element.Clone(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private sealed class CaptureStream(Stream inner) : Stream
    {
        private readonly MemoryStream _capture = new(CaptureLimit);
        public long Total { get; private set; }
        public ReadOnlyMemory<byte> Bytes => _capture.ToArray();
        private void Record(ReadOnlySpan<byte> data)
        {
            Total += data.Length;
            var remaining = CaptureLimit - (int)_capture.Length;
            if (remaining > 0) _capture.Write(data[..Math.Min(data.Length, remaining)]);
        }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Record(buffer.AsSpan(offset, n)); return n;
        }
        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            Record(buffer[..n]); return n;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var n = await inner.ReadAsync(buffer, offset, count, ct);
            Record(buffer.AsSpan(offset, n)); return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            Record(buffer.Span[..n]); return n;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count); Record(buffer.AsSpan(offset, count));
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer); Record(buffer);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await inner.WriteAsync(buffer, offset, count, ct); Record(buffer.AsSpan(offset, count));
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await inner.WriteAsync(buffer, ct); Record(buffer.Span);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _capture.Dispose(); // underlying stream belongs to ASP.NET Core
            base.Dispose(disposing);
        }
    }
}
