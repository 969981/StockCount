using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockCount.Server.Hosting;

namespace StockCount.Server.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly WindowsCredentialVault _vault;
    private readonly ServerRuntime _runtime;
    private readonly LogReader _logs;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _settingsInvalid;

    [ObservableProperty] private ServerSettings settings = new();
    [ObservableProperty] private string sqlPasswordInput = "";
    [ObservableProperty] private string certPasswordInput = "";
    [ObservableProperty] private string logSearch = "";
    [ObservableProperty] private string message = "准备就绪。";
    [ObservableProperty] private string serviceState = "未启动";
    [ObservableProperty] private string databaseState = "未测试";
    [ObservableProperty] private string credentialState = "未检查";
    [ObservableProperty] private string apiUrl = "http://127.0.0.1:5080";
    [ObservableProperty] private ObservableCollection<ServerLog> logEntries = new();

    public string SettingsPath => _store.SettingsPath;
    public string LogPath => _store.LogPath;
    public bool IsRunning => _runtime.IsRunning;

    public MainViewModel(SettingsStore store, WindowsCredentialVault vault,
        ServerRuntime runtime, LogReader logs)
    {
        _store = store;
        _vault = vault;
        _runtime = runtime;
        _logs = logs;
        try { Settings = store.Load(); }
        catch (Exception ex)
        {
            _settingsInvalid = true;
            Message = "配置加载失败，已阻止覆盖： " + ex.Message;
        }
        ApiUrl = "http://127.0.0.1:" + Settings.ApiPort;
        try { CredentialState = _vault.Read("sql-password") is null ? "未保存 SQL 密码" : "SQL 密码已保存"; }
        catch (Exception ex) { CredentialState = "凭据读取失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_settingsInvalid) throw new InvalidOperationException("配置文件无效，请先修复或备份并移走原文件。");
            if (_runtime.IsRunning) throw new InvalidOperationException("服务运行中：请先停止服务再修改配置。");
            var old = _store.Load();
            var newSqlPassword = !string.IsNullOrEmpty(SqlPasswordInput);
            if (Settings.DatabaseIdentityChangedFrom(old, newSqlPassword))
                Settings.RevokeWriteApprovals();
            Settings.Validate();
            if (newSqlPassword) _vault.Write("sql-password", SqlPasswordInput);
            if (!string.IsNullOrEmpty(CertPasswordInput)) _vault.Write("cert-password", CertPasswordInput);
            _store.Save(Settings);
            SqlPasswordInput = "";
            CertPasswordInput = "";
            CredentialState = _vault.Read("sql-password") is null ? "未保存 SQL 密码" : "SQL 密码已保存";
            ApiUrl = "http://127.0.0.1:" + Settings.ApiPort;
            Message = "配置已保存至 EXE 同目录。数据库身份变化时写入授权已重置。";
        }
        catch (Exception ex) { Message = "保存失败：" + ex.Message; }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        try
        {
            if (_settingsInvalid) throw new InvalidOperationException("配置文件损坏，不能启动。");
            Message = "正在启动 API...";
            await _runtime.StartAsync(_store.Load());
            Message = "API 已启动，管理端口仅监听本机；LAN HTTP 请限制可信网段。";
        }
        catch (Exception ex) { Message = "启动失败：" + ex.Message; }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        try { await _runtime.StopAsync(); Message = "API 已停止。"; }
        catch (Exception ex) { Message = "停止失败：" + ex.Message; }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task TestDatabaseAsync()
    {
        try
        {
            DatabaseState = "测试中...";
            var success = await _runtime.TestDatabaseAsync(_store.Load());
            DatabaseState = success ? "SELECT 1 成功" : "连接失败";
            Message = success ? "SQL Server 连接成功，仍需核验业务表权限。" : "连接失败：检查地址、账号、证书及网络。";
        }
        catch (Exception ex) { DatabaseState = "连接失败"; Message = ex.Message; }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            var current = _store.Load();
            var status = await _runtime.StatusAsync(current);
            ServiceState = status.Running ? (status.Healthy ? "运行中 · 健康" : "运行中 · 未就绪") : "已停止";
            ApiUrl = (current.AllowLan ? "http://<服务器局域网IP>:" : "http://127.0.0.1:") + current.ApiPort;
            if (status.LastError is not null && !status.Running) Message = status.LastError;
            OnPropertyChanged(nameof(IsRunning));
        }
        catch (Exception ex) { Message = "状态检查失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try { LogEntries = new ObservableCollection<ServerLog>(_logs.Read(LogSearch)); }
        catch (Exception ex) { Message = "日志读取失败：" + ex.Message; }
        finally { _refreshGate.Release(); }
    }

    public string GetDeviceKeyForClipboard() => _vault.GetOrCreateDeviceKey();

    public async Task CloseAsync()
    {
        try { await _runtime.DisposeAsync(); }
        finally { _refreshGate.Dispose(); }
    }
}
