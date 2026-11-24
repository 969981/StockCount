using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace StockCount.Server.Hosting;

public sealed record ServerRuntimeStatus(bool Running, bool Healthy, int ApiPort, string? LastError);

/// <summary>Owns only the API child started by this process; never kills unrelated API processes.</summary>
public sealed class ServerRuntime(SettingsStore store, WindowsCredentialVault vault) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private Process? _child;
    private string _adminToken = "";
    private int _port = 5080;
    private string? _lastError;

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
        Put("STOCKCOUNT_LOG_DB_PATH", store.LogPath);
        return info;
    }

    private async Task<bool> HealthyOnLocalPortAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var result = await _http.GetAsync($"http://127.0.0.1:{port + 1}/health", ct);
            return result.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
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
            if (IsRunning) throw new InvalidOperationException("服务已经在当前管理器中运行。");
            if (_child is not null) { _child.Dispose(); _child = null; }
            if (await HealthyOnLocalPortAsync(settings.ApiPort, ct))
                throw new InvalidOperationException("管理端口上已有运行中的 API，请先停止另一管理器。");
            _adminToken = Guid.NewGuid().ToString("N");
            _port = settings.ApiPort;
            _lastError = null;
            var process = new Process { StartInfo = BuildStartInfo(settings, _adminToken) };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("操作系统未启动 API 子进程。");
                _child = process;
                for (var i = 0; i < 40; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited) break;
                    if (await HealthyOnLocalPortAsync(_port, ct)) return;
                    await Task.Delay(175, ct);
                }
                throw new InvalidOperationException(process.HasExited
                    ? $"API 子进程提前退出（退出码 {process.ExitCode}），请检查 SQLite 日志和端口。"
                    : "API 启动超时，健康检查未通过。");
            }
            catch
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
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
            _child = null; // a later status call must not describe a stopping process as live
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
                using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                try { await process.WaitForExitAsync(shutdownDeadline.Token); }
                catch (OperationCanceledException) { /* unresponsive process: only terminate our own child */ }
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            process.Dispose();
            _adminToken = "";
        }
        finally { _gate.Release(); }
    }

    public async Task<ServerRuntimeStatus> StatusAsync(ServerSettings settings)
    {
        var running = IsRunning;
        var healthy = running && await HealthyOnLocalPortAsync(_port);
        var error = _lastError;
        if (_child is { HasExited: true } exited)
            error = $"API 已退出，退出码 {exited.ExitCode}";
        return new ServerRuntimeStatus(running, healthy, settings.ApiPort, error);
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
        await StopAsync();
        _http.Dispose();
        _gate.Dispose();
    }
}
