using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Pad.Services;

public sealed class ScanApiException(HttpStatusCode statusCode, string code, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class StockCountApiClient : ICountScanGateway
{
    private readonly HttpClient _http = new() { Timeout=TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string EnvironmentName => Preferences.Default.Get("StockCountEnvironment","Test");
    public string BaseUrl
    {
        get
        {
            var saved = Preferences.Default.Get("StockCountApiUrl", "");
            return string.IsNullOrWhiteSpace(saved) ? ApiEndpointPolicy.DefaultEndpoint : saved.TrimEnd('/');
        }
    }

    public async Task ConfigureAsync(string baseUrl,string environment,string deviceKey)
    {
        if (!ApiEndpointPolicy.TryNormalize(baseUrl, out var normalized))
            throw new InvalidOperationException("API 地址须为 http://192.168.x.x:5080 等内网 IP，或 HTTPS 地址；HTTP 不允许公网域名/IP。");
        if (environment is not ("Test" or "Production"))
            throw new InvalidOperationException("环境必须为 Test 或 Production。");
        Preferences.Default.Set("StockCountApiUrl",normalized);
        Preferences.Default.Set("StockCountEnvironment",environment);
        if (!string.IsNullOrWhiteSpace(deviceKey))
            await SecureStorage.Default.SetAsync("StockCountDeviceKey",deviceKey);
    }

    private async Task<HttpRequestMessage> Message(HttpMethod method,string path,
        HttpContent? content=null)
    {
        if (string.IsNullOrEmpty(BaseUrl))
            throw new InvalidOperationException("请先在设置页填写后端 API 地址（例如 http://192.168.1.100:5080）。");
        var request = new HttpRequestMessage(method,BaseUrl+path) { Content=content };
        request.Headers.TryAddWithoutValidation("X-StockCount-Environment",EnvironmentName);
        var key=await SecureStorage.Default.GetAsync("StockCountDeviceKey");
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.TryAddWithoutValidation("X-StockCount-Device-Key",key);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var response=await _http.SendAsync(request,ct);
        if (response.IsSuccessStatusCode) return response;
        var code="HTTP_"+(int)response.StatusCode;
        var message="服务器拒绝操作。";
        try
        {
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.TryGetProperty("code",out var codeElement))
                code=codeElement.GetString()??code;
            if (document.RootElement.TryGetProperty("title",out var titleElement))
                message=titleElement.GetString()??message;
        }
        catch (JsonException) { }
        response.Dispose();
        throw new ScanApiException(response.StatusCode,code,message);
    }

    private async Task<T> ReadJsonAsync<T>(HttpMethod method,string path,object? payload,CancellationToken ct)
    {
        using var content=payload is null ? null : JsonContent.Create(payload,options:Json);
        using var request=await Message(method,path,content);
        using var response=await SendAsync(request,ct);
        return await response.Content.ReadFromJsonAsync<T>(Json,ct)
            ?? throw new InvalidOperationException("服务端返回了空响应。");
    }

    public async Task<CountPage> GetCountsAsync(string? cursor=null,string? search=null,
        CancellationToken ct=default) =>
        await ReadJsonAsync<CountPage>(HttpMethod.Get,
            "/api/v1/inventory-counts?pageSize=100" +
            (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor="+Uri.EscapeDataString(cursor)) +
            (string.IsNullOrWhiteSpace(search) ? "" : "&search="+Uri.EscapeDataString(search.Trim())),
            null,ct);

    /// <summary>Read a selected order directly; used to restore a crashed submission.</summary>
    public Task<CountDetails> GetCountDetailsAsync(string countNumber,
        CancellationToken ct=default) =>
        ReadJsonAsync<CountDetails>(HttpMethod.Get,
            "/api/v1/inventory-counts/"+Uri.EscapeDataString(countNumber),null,ct);

    /// <summary>Read-only ERP count lines for the prototype's history screen.</summary>
    public Task<CountItemPage> GetItemsAsync(string countNumber,string? cursor=null,
        CancellationToken ct=default) =>
        ReadJsonAsync<CountItemPage>(HttpMethod.Get,
            "/api/v1/inventory-counts/"+Uri.EscapeDataString(countNumber)+
            "/items?pageSize=50"+
            (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor="+Uri.EscapeDataString(cursor)),
            null,ct);

    public async Task<LocationScanResult> ResolveLocationAsync(string countNumber,string rawCode,CancellationToken ct)
    {
        try
        {
            var result=await ReadJsonAsync<ResolvedLocation>(
                HttpMethod.Post,"/api/v1/locations/resolve",
                new ResolveLocationRequest(countNumber,rawCode),ct);
            return result.IsValid && !string.IsNullOrWhiteSpace(result.LocationCode)
                ? new LocationScanResult(result.LocationCode)
                : new LocationScanResult(null, "LOCATION_INVALID",
                    LocationScanPolicy.Describe("LOCATION_INVALID"));
        }
        catch (ScanApiException e) when (e.Code is "LOCATION_INVALID" or "LOCATION_FORMAT_INVALID" or
            "LOCATION_NOT_FOUND" or "LOCATION_OUTSIDE_WAREHOUSE" or "LOCATION_AMBIGUOUS" or
            "COUNT_NOT_FOUND" or "LOCATION_LOOKUP_UNAVAILABLE" or "LOCATION_REQUEST_INVALID")
        {
            return new LocationScanResult(null, e.Code,
                e.Code == "LOCATION_REQUEST_INVALID" ? e.Message : LocationScanPolicy.Describe(e.Code));
        }
        catch (ScanApiException e)
        {
            var message = (int)e.StatusCode is 401 or 403
                ? "库位查询被后台拒绝，请检查设备访问权限。"
                : "库位查询失败，请检查后台服务或数据库连接（HTTP " + (int)e.StatusCode + "）。";
            return new LocationScanResult(null, "LOCATION_LOOKUP_UNAVAILABLE", message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            return new LocationScanResult(null, "LOCATION_LOOKUP_UNAVAILABLE",
                "库位查询失败：" + ConnectionDiagnostics.DescribeException(e));
        }
    }

    public async Task<BoxScanData?> ResolveLabelAsync(string countNumber,string qr,CancellationToken ct)
    {
        try
        {
            var result=await ReadJsonAsync<ResolvedLabel>(HttpMethod.Post,
                "/api/v1/labels/resolve",new ResolveLabelRequest(countNumber,qr),ct);
            return new BoxScanData(result.BoxNumber,result.ItemCode,result.ItemName,
                result.Specification,result.LotNumber,result.StockUnit,
                result.AlreadyRecordedInCount);
        }
        catch (ScanApiException e) when (e.Code=="LABEL_NOT_FOUND")
        {
            return null;
        }
    }

    public async Task<bool> SubmitAsync(string countNumber,Guid requestId,string qr,
        string boxNumber,string location,decimal qty,CancellationToken ct)
    {
        using var request=await Message(HttpMethod.Post,
            $"/api/v1/inventory-counts/{Uri.EscapeDataString(countNumber)}/items",
            JsonContent.Create(new SaveCountItemRequest(qr,boxNumber,location,qty),options:Json));
        request.Headers.TryAddWithoutValidation("Idempotency-Key",requestId.ToString("D"));
        HttpResponseMessage response;
        try { response=await SendAsync(request,ct); }
        catch(ScanApiException error) when (error.Code is
            "BOX_ALREADY_RECORDED" or "PRODUCTION_WRITE_DISABLED" or
            "ERP_WRITE_UNAVAILABLE" or "SCHEMA_NOT_APPROVED" or
            "COUNT_QUANTITY_INVALID" or "COUNT_NOT_WRITABLE" or
            "UNIT_MAPPING_UNSUPPORTED" or "LOCATION_INVALID" or
            "BOX_MISMATCH" or "LABEL_NOT_FOUND" or "LABEL_AMBIGUOUS" or "ERP_SCHEMA_UNSUPPORTED" or "ERP_SCHEMA_UNAVAILABLE" or "AUDIT_USER_NOT_CONFIGURED")
        {
            throw new ScanCommitRejectedException(error.Code,error.Message);
        }
        using (response)
        {
            var item=await response.Content.ReadFromJsonAsync<SaveCountItemResult>(Json,ct);
            return item is not null && item.Status=="Committed" &&
                item.RequestId==requestId &&
                item.SavedQtyStu==qty && item.SavedQtyPcu==qty;
        }

    }

    public async Task<bool?> GetSubmissionAsync(string countNumber,Guid requestId,CancellationToken ct)
    {
        try
        {
            var saved=await ReadJsonAsync<SaveCountItemResult>(
                HttpMethod.Get,
                $"/api/v1/submissions/{requestId:D}?countNumber={Uri.EscapeDataString(countNumber)}",
                null,ct);
            return saved.Status=="Committed";
        }
        catch (ScanApiException e) when (e.Code=="SUBMISSION_NOT_FOUND") { return false; }
        catch (ScanApiException e) when (e.Code=="SUBMISSION_LOOKUP_UNAVAILABLE") { return null; }
    }

    public async Task<bool> HasDeviceKeyAsync() =>
        !string.IsNullOrWhiteSpace(
            await SecureStorage.Default.GetAsync("StockCountDeviceKey"));

    /// <summary>Probe the address currently entered in Settings without saving or writing ERP data.</summary>
    public async Task DiagnoseConnectionAsync(string address, string environment, string enteredKey,
        Action<string> report)
    {
        if (!ApiEndpointPolicy.TryNormalize(address, out var normalized))
        {
            report("✗ API 地址格式无效。请输入内网 IP:端口或完整 HTTPS 地址。");
            return;
        }

        var endpoint = new Uri(normalized);
        var lines = new List<string>
        {
            $"目标：{normalized}",
            $"帐套：{(environment == "Production" ? "COB" : "XTEST")}"
        };
        void Add(string line) { lines.Add(line); report(string.Join("\n", lines)); }
        Add("正在检测 TCP 端口…");
        var tcp = await ConnectionDiagnostics.ProbeTcpAsync(endpoint.Host, endpoint.Port);
        Add(tcp.Detail);
        if (!tcp.Reachable)
        {
            Add(await ConnectionDiagnostics.ProbeIcmpAsync(endpoint.Host));
            Add("请核对 Windows ipconfig、PDA Wi-Fi、API 是否启动、是否允许局域网监听及防火墙入站规则。");
            Add("API / SQL Server：尚未检测（TCP 未连接）。");
            return;
        }

        // Do not forward saved credentials to a different, unsaved server.
        var key = enteredKey;
        if (string.IsNullOrWhiteSpace(key) &&
            string.Equals(normalized, BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            try { key = await SecureStorage.Default.GetAsync("StockCountDeviceKey") ?? ""; }
            catch (Exception) { Add("旧版设备密钥读取失败；仍可检测免密服务。"); }
        }
        var probeId = Guid.NewGuid().ToString("D");
        Add("诊断编号：" + probeId);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(10) };
        async Task<HttpResponseMessage> Probe(string path, bool authenticated)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, normalized + path);
            req.Headers.TryAddWithoutValidation("X-StockCount-Environment", environment);
            req.Headers.TryAddWithoutValidation("X-StockCount-Connection-Test", "1");
            req.Headers.TryAddWithoutValidation("X-StockCount-Diagnostic-Id", probeId);
            if (authenticated && !string.IsNullOrWhiteSpace(key))
                req.Headers.TryAddWithoutValidation("X-StockCount-Device-Key", key);
            return await http.SendAsync(req);
        }

        try
        {
            Add("正在检查 API /health…");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var health = await Probe("/health", false);
            if (!health.IsSuccessStatusCode)
            {
                Add($"✗ API 健康检查：HTTP {(int)health.StatusCode}。端口可达，但服务地址或协议可能不正确。");
                return;
            }
            using var body = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            if (body.RootElement.ValueKind != JsonValueKind.Object ||
                !body.RootElement.TryGetProperty("service", out var service) ||
                service.ValueKind != JsonValueKind.String ||
                service.GetString() != "StockCount.Api")
            {
                Add("✗ 该端口返回了 HTTP 响应，但不是 StockCount API。请检查端口。");
                return;
            }
            Add($"✓ StockCount API 可达（HTTP {(int)health.StatusCode}，{watch.ElapsedMilliseconds} ms）。");
        }
        catch (JsonException)
        {
            Add("✗ 端口可达，但 /health 未返回 StockCount JSON；可能连接了其他服务。");
            return;
        }
        catch (Exception error)
        {
            Add("✗ API 健康检查：" + ConnectionDiagnostics.DescribeException(error));
            return;
        }

        try
        {
            Add("正在检查设备访问权限与 SQL Server /ready…");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var ready = await Probe("/ready", true);
            switch ((int)ready.StatusCode)
            {
                case 200:
                    using (var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync()))
                    {
                        if (body.RootElement.ValueKind != JsonValueKind.Object ||
                            !body.RootElement.TryGetProperty("database", out var db) ||
                            db.ValueKind != JsonValueKind.String || db.GetString() != "Reachable")
                        {
                            Add("✗ /ready 响应格式不正确，无法确认数据库连通。");
                            return;
                        }
                    }
                    Add($"✓ 设备访问成功；SQL Server SELECT 1 成功（{watch.ElapsedMilliseconds} ms）。");
                    Add("连接正常。此测试不执行写入，不代表盘点写入权限已通过。");
                    break;
                case 401:
                    Add("✗ HTTP 401：设备密钥未授权。请开启后台局域网免密模式，或配置旧版密钥。");
                    break;
                case 403:
                    Add("✗ HTTP 403：服务拒绝访问。请检查后台免密设置、来源 IP 和访问策略。");
                    break;
                case 503:
                    Add("✗ HTTP 503：API 已收到请求，但数据库未就绪。请在后台测试 SQL Server 连接、账号密码和数据库权限。");
                    break;
                default:
                    Add($"✗ /ready 返回 HTTP {(int)ready.StatusCode}，请检查后台 SQLite 日志。");
                    break;
            }
        }
        catch (JsonException) { Add("✗ /ready 响应格式不正确，无法确认数据库连通。"); }
        catch (Exception error) { Add("✗ 数据库就绪检查：" + ConnectionDiagnostics.DescribeException(error)); }
    }

    /// <summary>Only checks that ASP.NET Core is alive; distinct from SQL /ready.</summary>
    public async Task<bool> PingServerAsync(CancellationToken ct=default)
    {
        using var request=await Message(HttpMethod.Get,"/health");
        using var response=await _http.SendAsync(request,ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PingDatabaseAsync(CancellationToken ct=default)
    {
        using var request=await Message(HttpMethod.Get,"/ready");
        using var response=await SendAsync(request,ct);
        return response.IsSuccessStatusCode;
    }
}
