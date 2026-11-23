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
    public string BaseUrl => Preferences.Default.Get("StockCountApiUrl","").TrimEnd('/');

    public async Task ConfigureAsync(string baseUrl,string environment,string deviceKey)
    {
        if (!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri) ||
            !ApiEndpointPolicy.IsAllowed(uri))
            throw new InvalidOperationException("API 地址须为 http://192.168.x.x:5080 等内网 IP，或 HTTPS 地址；HTTP 不允许公网域名/IP。");
        if (environment is not ("Test" or "Production"))
            throw new InvalidOperationException("环境必须为 Test 或 Production。");
        Preferences.Default.Set("StockCountApiUrl",baseUrl.TrimEnd('/'));
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

    public async Task<string?> ResolveLocationAsync(string countNumber,string rawCode,CancellationToken ct)
    {
        try
        {
            var result=await ReadJsonAsync<ResolvedLocation>(
                HttpMethod.Post,"/api/v1/locations/resolve",
                new ResolveLocationRequest(countNumber,rawCode),ct);
            return result.IsValid ? result.LocationCode : null;
        }
        catch (ScanApiException e) when (e.Code is "LOCATION_INVALID" or "COUNT_NOT_FOUND")
        {
            return null;
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
            "BOX_MISMATCH" or "ERP_SCHEMA_UNSUPPORTED" or "AUDIT_USER_NOT_CONFIGURED")
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
