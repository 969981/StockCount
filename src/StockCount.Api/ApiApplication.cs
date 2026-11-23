using System.Net;
using System.Security.Cryptography;
using System.Text;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Infrastructure;

namespace StockCount.Api;

/// <summary>
/// Share the exact same API routes between standalone CLI and the Tauri 2 C# Sidecar.
/// The host supplies SQL settings, Kestrel listeners and a SQLite logging provider.
/// </summary>
public static class ApiApplication
{
    public static WebApplication Build(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<IInventoryReadRepository>(services =>
            new SqlInventoryReadRepository(
                services.GetRequiredService<IConfiguration>().GetConnectionString("SageX3") ?? ""));
        builder.Services.AddScoped<IInventoryWriteRepository>(services =>
        {
            var config = services.GetRequiredService<IConfiguration>();
            var options = new CountWriteOptions(
                config.GetValue<bool>("StockCount:EnableTestWrites"),
                config.GetValue<bool>("StockCount:EnableProductionWrites"),
                config["StockCount:Write:AuditUser"] ?? "",
                config.GetValue<bool>("StockCount:Write:SchemaReviewed"));
            return new SqlInventoryWriteRepository(config.GetConnectionString("SageX3") ?? "", options);
        });
        
        var app = builder.Build();
        app.UseExceptionHandler();

        // Persist structured HTTP access records through the configured ILogger provider.
        // Request bodies (including scanned QR data) and Authorization/DeviceKey headers are never logged.
        app.Use(async (context, next) =>
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try { await next(); }
            finally
            {
                started.Stop();
                if (!context.Request.Path.StartsWithSegments("/health"))
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("StockCount.Api.Http");
                    logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} ({DurationMs} ms)",
                        context.Request.Method, context.Request.Path.Value,
                        context.Response.StatusCode, started.ElapsedMilliseconds);
                }
            }
        });

        // No user login screen: remote requests still require server-side device authorization.
        // If no key has been configured, only loopback requests are permitted.
        var deviceKey = app.Configuration["StockCount:DeviceKey"];
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var guarded = path.StartsWithSegments("/api/v1") || path.StartsWithSegments("/ready");
            if (guarded)
            {
                if (string.IsNullOrEmpty(deviceKey))
                {
                    var remote = context.Connection.RemoteIpAddress;
                    var isLocal = remote is not null
                        && (IPAddress.IsLoopback(remote)
                            || (remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4())));
                    if (!isLocal)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new { code = "DEVICE_ACCESS_NOT_CONFIGURED" });
                        return;
                    }
                }
                else
                {
                    var supplied = context.Request.Headers["X-StockCount-Device-Key"].ToString();
                    var inputBytes = Encoding.UTF8.GetBytes(supplied);
                    var expectedBytes = Encoding.UTF8.GetBytes(deviceKey);
                    if (supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(inputBytes, expectedBytes))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { code = "DEVICE_NOT_AUTHORIZED" });
                        return;
                    }
                }
            }
            await next();
        });
        
        
        // Desktop-only graceful shutdown. Requires a per-process random secret
        // AND an actual loopback peer; no public API route can trigger this.
        app.MapPost("/internal/shutdown", (HttpContext context,
            IHostApplicationLifetime lifetime) =>
        {
            var token = app.Configuration["StockCount:AdminToken"];
            var supplied = context.Request.Headers["X-StockCount-Admin"].ToString();
            var address = context.Connection.RemoteIpAddress;
            var loopback = address is not null &&
                (IPAddress.IsLoopback(address) ||
                 (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));
            if (!loopback || string.IsNullOrWhiteSpace(token) ||
                supplied.Length > 256 ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(supplied),
                    Encoding.UTF8.GetBytes(token)))
                return Results.NotFound();

            context.Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.Accepted();
        });

        // Liveness only: does not imply that SQL Server is reachable.
        app.MapGet("/health", () => Results.Ok(new
        {
            service = "StockCount.Api",
            status = "Running",
            version = "0.2.0-transactional",
            utc = DateTimeOffset.UtcNow
        }));
        
        app.MapGet("/ready", async (HttpContext context, IInventoryReadRepository repository,
            ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            try
            {
                await repository.PingAsync(cancellationToken);
                return Results.Ok(new { environment = environment.Name, database = "Reachable" });
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Database readiness failed for {Environment}", environment.Name);
                return Error(503, "DATABASE_UNAVAILABLE", "数据库暂时不可访问或尚未配置。", environment.Name);
            }
        });
        
        app.MapGet("/api/v1/inventory-counts",
            async (HttpContext context, IInventoryReadRepository repository, int? pageSize,
                string? cursor, string? search, CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (!TryPage(pageSize, cursor, out var size, out var skip))
                return Error(400, "PAGINATION_INVALID", "pageSize 必须在 1..100，cursor 必须是有效偏移量。", environment.Name);
            if (search?.Length > 100)
                return Error(400, "SEARCH_TOO_LONG", "搜索文本不能超过 100 个字符。", environment.Name);
        
            var rows = await repository.GetCountsAsync(environment, skip, size + 1,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(), cancellationToken);
            var next = rows.Count > size ? (skip + size).ToString() : null;
            return Results.Ok(new CountPage(environment.Name, rows.Take(size).ToArray(), next));
        });
        
        app.MapGet("/api/v1/inventory-counts/{countNumber}",
            async (HttpContext context, string countNumber, IInventoryReadRepository repository,
                CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (string.IsNullOrWhiteSpace(countNumber) || countNumber.Length > 30)
                return Error(400, "COUNT_NUMBER_INVALID", "盘点单号不合法。", environment.Name);
            var count = await repository.GetCountAsync(environment, countNumber, cancellationToken);
            return count is null
                ? Error(404, "COUNT_NOT_FOUND", "未找到对应盘点单。", environment.Name)
                : Results.Ok(new CountDetails(environment.Name, count));
        });
        
        app.MapPost("/api/v1/locations/resolve",
            async (HttpContext context, ResolveLocationRequest request, IInventoryReadRepository repository,
                CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (string.IsNullOrWhiteSpace(request.CountNumber) || request.CountNumber.Length > 30 ||
                string.IsNullOrWhiteSpace(request.RawCode) || request.RawCode.Length > 200)
                return Error(400, "LOCATION_REQUEST_INVALID", "请输入有效盘点单号与库位码。", environment.Name);
            var count = await repository.GetCountAsync(environment, request.CountNumber, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND", "未找到对应盘点单。", environment.Name);
        
            // Scan protocol (e.g. LC. prefix) has NOT yet been field-verified.
            // V0.1 only accepts a raw code exactly equal to STOLOC.LOC_0.
            var locationCode = request.RawCode.Trim();
            if (locationCode.Length > 10)
                return Error(422, "LOCATION_INVALID", "盘点库位超过 XCOUTD.LOC_0 的 10 字符上限。", environment.Name);
        
            var location = await repository.FindLocationAsync(environment, count.SiteCode,
                count.WarehouseCode, locationCode, cancellationToken);
            return location is null
                ? Error(422, "LOCATION_INVALID", "当前站点与仓库不存在该库位，或扫码协议尚未适配。", environment.Name)
                : Results.Ok(new ResolvedLocation(environment.Name, location.LocationCode,
                    location.SiteCode, location.WarehouseCode, true, null));
        });
        
        app.MapPost("/api/v1/labels/resolve",
            async (HttpContext context, ResolveLabelRequest request, IInventoryReadRepository repository,
                CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (string.IsNullOrWhiteSpace(request.CountNumber) || request.CountNumber.Length > 30 ||
                string.IsNullOrWhiteSpace(request.RawQrCode) || request.RawQrCode.Length > 200)
                return Error(400, "BARCODE_UNSUPPORTED", "请输入盘点单号与完整标签二维码。", environment.Name);
            var count = await repository.GetCountAsync(environment, request.CountNumber, cancellationToken);
            if (count is null) return Error(404, "COUNT_NOT_FOUND", "未找到对应盘点单。", environment.Name);
        
            // Match XLABELDTL.QRCOD_0, not a client-controlled item/quantity/label status.
            // IMPORTANT: STA_0 must not be used as an admission filter.
            var label = await repository.FindLabelAsync(environment, request.CountNumber,
                request.RawQrCode.TrimEnd('\r','\n'), cancellationToken);
            if (label is null)
                return Error(404, "LABEL_NOT_FOUND", "未查询到完整二维码对应的标签。", environment.Name);
        
            return Results.Ok(new ResolvedLabel(
                environment.Name, label.BoxNumber, label.ItemCode, label.ItemName,
                label.Specification, label.LotNumber, label.StockUnit,
                label.LabelQty, label.LabelStatusCode, label.AlreadyRecordedInCount));
        });
        
        app.MapGet("/api/v1/inventory-counts/{countNumber}/items",
            async (HttpContext context, string countNumber, IInventoryReadRepository repository,
                int? pageSize, string? cursor, CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (string.IsNullOrWhiteSpace(countNumber) || countNumber.Length > 30)
                return Error(400, "COUNT_NUMBER_INVALID", "盘点单号不合法。", environment.Name);
            if (!TryPage(pageSize, cursor, out var size, out var skip))
                return Error(400, "PAGINATION_INVALID", "分页参数不合法。", environment.Name);
            if (await repository.GetCountAsync(environment, countNumber, cancellationToken) is null)
                return Error(404, "COUNT_NOT_FOUND", "未找到对应盘点单。", environment.Name);
            var rows = await repository.GetItemsAsync(environment, countNumber, skip, size + 1, cancellationToken);
            var next = rows.Count > size ? (skip + size).ToString() : null;
            return Results.Ok(new CountItemPage(environment.Name, rows.Take(size).ToArray(), next));
        });
        
        // Idempotent, transactional write. Actual mutation requires explicit
        // server-side environment + schema-approval flags AND DBA journal migration.
        app.MapPost("/api/v1/inventory-counts/{countNumber}/items",
            async (HttpContext context, string countNumber, SaveCountItemRequest request,
                IInventoryWriteRepository repository, ILogger<SqlInventoryWriteRepository> logger,
                CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            var keyHeader = context.Request.Headers["Idempotency-Key"].ToString();
            if (!Guid.TryParse(keyHeader, out var requestId) || requestId == Guid.Empty)
                return Error(400, "IDEMPOTENCY_KEY_REQUIRED",
                    "Idempotency-Key 必须为非空 UUID；同一笔请求重试要复用原 UUID。", environment.Name);
            try
            {
                var saved = await repository.SubmitAsync(
                    environment, countNumber, requestId, request, cancellationToken);
                var location = $"/api/v1/submissions/{requestId}?countNumber={Uri.EscapeDataString(countNumber)}";
                return saved.Replayed
                    ? Results.Ok(saved)
                    : Results.Created(location, saved);
            }
            catch (CountWriteException error)
            {
                logger.LogWarning("Count submit rejected: {Code}, {Environment}, {RequestId}",
                    error.Code, environment.Name, requestId);
                return Error(error.StatusCode, error.Code, error.Message, environment.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Error(503, "SUBMISSION_OUTCOME_UNKNOWN",
                    "请求已取消，提交结果可能不确定。请使用原 Idempotency-Key 查询结果，勿换新请求ID。", environment.Name);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Count submission uncertain: {Environment}, {RequestId}",
                    environment.Name, requestId);
                return Error(503, "SUBMISSION_OUTCOME_UNKNOWN",
                    "提交状态未知，请使用原请求ID查询结果，切勿生成新请求重试。", environment.Name);
            }
        });

        app.MapGet("/api/v1/submissions/{requestId:guid}",
            async (HttpContext context, Guid requestId, string? countNumber,
                IInventoryWriteRepository repository, ILogger<SqlInventoryWriteRepository> logger,
                CancellationToken cancellationToken) =>
        {
            if (!TryEnvironment(context, out var environment)) return BadEnvironment();
            if (string.IsNullOrWhiteSpace(countNumber) || countNumber.Length > 30)
                return Error(400, "COUNT_NUMBER_INVALID",
                    "查询提交结果必须指定盘点单号 ?countNumber=。", environment.Name);
            try
            {
                var stored = await repository.GetSubmissionAsync(
                    environment, countNumber, requestId, cancellationToken);
                return stored is null
                    ? Error(404, "SUBMISSION_NOT_FOUND",
                        "未找到该请求的已提交记录；如之前发生网络超时，请以原 UUID 安全重试。", environment.Name)
                    : Results.Ok(stored);
            }
            catch (CountWriteException error)
            {
                return Error(error.StatusCode, error.Code, error.Message, environment.Name);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Submission lookup failed: {Environment}, {RequestId}",
                    environment.Name, requestId);
                return Error(503, "SUBMISSION_LOOKUP_UNAVAILABLE",
                    "提交日志暂时不可查询，不能据此断言 ERP 未写入。", environment.Name);
            }
        });
        
        return app;
    }

    static bool TryEnvironment(HttpContext context, out SelectedEnvironment environment) =>
        EnvironmentSelector.TryResolve(
            context.Request.Headers["X-StockCount-Environment"].ToString(), out environment);
    
    static bool TryPage(int? pageSize, string? cursor, out int size, out int skip)
    {
        size = pageSize ?? 50;
        skip = 0;
        return size is >= 1 and <= 100
            && (string.IsNullOrEmpty(cursor) || (int.TryParse(cursor, out skip) && skip >= 0))
            && skip <= 1_000_000;
    }
    
    static IResult BadEnvironment() =>
        Error(400, "ENVIRONMENT_INVALID",
            "必须在 X-StockCount-Environment 请求头中指定 Test 或 Production。");
    
    static IResult Error(int status, string code, string title, string? environment = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (environment is not null) extensions["environment"] = environment;
        return Results.Problem(statusCode: status, title: title, extensions: extensions);
    }
}
