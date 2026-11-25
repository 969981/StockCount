using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace StockCount.Server.Hosting;

public sealed record ServerRuntimeStatus(bool Running, bool Healthy, int ApiPort, string? LastError,
    string State, IReadOnlyList<PortOwner> Owners, PortOwner? ExternalApi = null);

/// <summary>Manages its own child; external APIs require verified port ownership and explicit operator confirmation.</summary>
public sealed class ServerRuntime(SettingsStore store, WindowsCredentialVault vault) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMilliseconds(900) };
    private Process? _child;
    private string _adminToken = "";
    private int _port = 5080;
    private string? _lastError;
    private int _disposing;

    public bool IsRunning => _child is { HasExited: false };

    private ProcessStartInfo BuildStartInfo(ServerSettings settings, string adminToken, bool dbTest = false)
    {
        var executable = Path.Combine(store.DirectoryPath, "StockCount.Api.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("找不到 StockCount.Api.exe：先构建并复制 API Sidecar 到桌面 EXE 同目录。", executable);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = store.DirectoryPath
        };
        if (dbTest) info.ArgumentList.Add("--test-db");
        void Put(string key, string value) => info.Environment[key] = value;
        Put("STOCKCOUNT_SQL_SERVER", settings.SqlServer);
        Put("STOCKCOUNT_SQL_DATABASE", settings.SqlDatabase);
        Put("STOCKCOUNT_SQL_INTEGRATED", settings.IntegratedSecurity.ToString());
        Put("STOCKCOUNT_SQL_USER", settings.SqlUser);
        Put("STOCKCOUNT_SQL_PASSWORD", vault.Read("sql-password") ?? "");
        Put("STOCKCOUNT_SQL_NO_ENCRYPT", (!settings.EncryptSql).ToString());
        Put("STOCKCOUNT_SQL_TRUST_CERT", settings.TrustServerCertificate.ToString());
        Put("STOCKCOUNT_XTEST_CHECK_SITE", settings.TestCheckSite.ToString());
        Put("STOCKCOUNT_XTEST_CHECK_WAREHOUSE", settings.TestCheckWarehouse.ToString());
        Put("STOCKCOUNT_COB_CHECK_SITE", settings.ProductionCheckSite.ToString());
        Put("STOCKCOUNT_COB_CHECK_WAREHOUSE", settings.ProductionCheckWarehouse.ToString());
        Put("STOCKCOUNT_ENABLE_TEST_WRITES", settings.EnableTestWrites.ToString());
        Put("STOCKCOUNT_ENABLE_PRODUCTION_WRITES", settings.EnableProductionWrites.ToString());
        Put("STOCKCOUNT_XTEST_SCHEMA_REVIEWED", settings.SchemaReviewed.ToString());
        Put("STOCKCOUNT_COB_SCHEMA_REVIEWED", settings.ProductionSchemaReviewed.ToString());
        Put("STOCKCOUNT_X3_AUDIT_USER", settings.EffectiveX3AuditUser);
        Put("STOCKCOUNT_API_PORT", settings.ApiPort.ToString());
        Put("STOCKCOUNT_ALLOW_LAN", settings.AllowLan.ToString());
        Put("STOCKCOUNT_CERT_PATH", settings.CertPath);
        Put("STOCKCOUNT_CERT_PASSWORD", vault.Read("cert-password") ?? "");
        Put("STOCKCOUNT_ALLOW_KEYLESS_LAN", (settings.AllowLan && settings.AllowKeylessLan).ToString());
        Put("STOCKCOUNT_DEVICE_KEY", settings.AllowLan && settings.AllowKeylessLan ? "" : vault.GetOrCreateDeviceKey());
        Put("STOCKCOUNT_ADMIN_TOKEN", adminToken);
        using var manager = Process.GetCurrentProcess();
        Put("STOCKCOUNT_MANAGER_PID", manager.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Put("STOCKCOUNT_MANAGER_STARTED_UTC_TICKS", manager.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Put("STOCKCOUNT_LOG_DB_PATH", store.LogPath);
        return info;
    }

    private async Task<bool> HealthyOnLocalPortAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var result = await _http.GetAsync($"http://127.0.0.1:{port + 1}/health", ct);
            if (!result.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("service", out var service) &&
                service.ValueKind == JsonValueKind.String && service.GetString() == "StockCount.Api";
        }
        catch (HttpRequestException) { return false; }
        catch (JsonException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public async Task StartAsync(ServerSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        store.EnsureWritableDirectory();
        if (settings.AllowLan && !settings.AllowKeylessLan && string.IsNullOrEmpty(vault.GetOrCreateDeviceKey()))
            throw new InvalidOperationException("开启 HTTP LAN 必须有设备访问密钥。");
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            if (IsRunning) throw new InvalidOperationException("服务已经在当前管理器中运行。");
            if (_child is not null) { _child.Dispose(); _child = null; }
            var occupied = PortInspection.Read(settings.ApiPort);
            if (occupied.Count > 0)
                throw new InvalidOperationException("端口已占用：" + string.Join("；", occupied.Select(x => x.Display)) +
                    "。请在服务总览查看进程并确认处理。");
            _adminToken = Guid.NewGuid().ToString("N");
            _port = settings.ApiPort;
            _lastError = null;
            var process = new Process { StartInfo = BuildStartInfo(settings, _adminToken) };
            var started = false;
            try
            {
                if (!process.Start()) throw new InvalidOperationException("操作系统未启动 API 子进程。");
                started = true;
                _child = process;
                for (var i = 0; i < 40; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited) break;
                    if (await HealthyOnLocalPortAsync(_port, ct))
                    {
                        var owners = PortInspection.Read(_port);
                        if (owners.Any(x => x.Port == _port) && owners.Any(x => x.Port == _port + 1) &&
                            owners.All(x => x.ProcessId == process.Id)) return;
                    }
                    await Task.Delay(175, ct);
                }
                throw new InvalidOperationException(process.HasExited
                    ? $"API 子进程提前退出（退出码 {process.ExitCode}），请检查 SQLite 日志和端口。"
                    : "API 启动超时，健康检查未通过。");
            }
            catch
            {
                if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
                process.Dispose();
                _child = null;
                _adminToken = "";
                _lastError = "API 启动失败或超时。";
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_child is null) return;
            var process = _child;
            // Keep ownership until stop has succeeded; status is serialized by the gate.
            if (!process.HasExited)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port + 1}/internal/shutdown");
                    request.Headers.Add("X-StockCount-Admin", _adminToken);
                    using var _ = await _http.SendAsync(request);
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                try { await process.WaitForExitAsync(shutdownDeadline.Token); }
                catch (OperationCanceledException) { /* unresponsive process: only terminate our own child */ }
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            process.Dispose();
            _child = null;
            _adminToken = "";
        }
        finally { _gate.Release(); }
    }

    public async Task<ServerRuntimeStatus> StatusAsync(ServerSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            var running = IsRunning;
            var port = running ? _port : settings.ApiPort;
            var owners = PortInspection.Read(port);
            var healthy = owners.Any(x => x.Port == port + 1) && await HealthyOnLocalPortAsync(port);
            var error = _lastError;
            if (_child is { HasExited: true } exited)
                error = $"API 已退出，退出码 {exited.ExitCode}";
            if (running)
            {
                healthy = healthy && owners.Any(x => x.Port == port) &&
                    owners.All(x => x.ProcessId == _child!.Id);
                return new(true, healthy, port, error, healthy ? "OwnedHealthy" : "OwnedUnhealthy", owners);
            }
            var external = PortInspection.ConfirmedExternalApi(owners, port, healthy);
            return new(false, external is not null, port, error,
                external is not null ? "ExternalApi" : owners.Count > 0 ? "PortConflict" : "Stopped",
                owners, external);
        }
        finally { _gate.Release(); }
    }

    public async Task StopExternalAsync(int port, PortOwner expected)
    {
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            if (IsRunning) throw new InvalidOperationException("当前管理器已有服务，禁止终止外部进程。");
            using var process = Process.GetProcessById(expected.ProcessId);
            _ = process.Handle;
            var current = PortInspection.ConfirmedExternalApi(PortInspection.Read(port), port,
                await HealthyOnLocalPortAsync(port));
            if (current is null || current != expected || process.HasExited ||
                process.StartTime.ToUniversalTime().Ticks != expected.StartedUtcTicks ||
                !string.Equals(process.MainModule?.FileName, expected.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("占用进程身份已改变或无法确认，未执行停止。");
            // No previous manager's admin token is available. Only the explicitly
            // confirmed, still-identical StockCount API may be terminated.
            process.Kill();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(deadline.Token);
            if (PortInspection.Read(port).Count > 0)
                throw new InvalidOperationException("旧 API 已退出，但端口仍有监听，请刷新检查。");
            _lastError = null;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TestDatabaseAsync(ServerSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        if (string.IsNullOrWhiteSpace(settings.SqlServer))
            throw new InvalidOperationException("请先填写并保存 SQL Server 地址。");
        using var check = new Process { StartInfo = BuildStartInfo(settings, "", dbTest: true) };
        if (!check.Start()) throw new InvalidOperationException("无法启动数据库测试进程。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try { await check.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!check.HasExited) { check.Kill(entireProcessTree: true); await check.WaitForExitAsync(); }
            throw new TimeoutException("数据库测试超时：请检查服务器地址及网络连接。");
        }
        return check.ExitCode == 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) return;
        try { await StopAsync(); }
        finally { _http.Dispose(); }
        // Do not dispose the gate while previously queued status operations unwind.
    }
}
