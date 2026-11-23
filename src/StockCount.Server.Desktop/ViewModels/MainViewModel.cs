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
    private bool _initialized;

    [ObservableProperty] private ServerSettings settings = new();
    [ObservableProperty] private string sqlPasswordInput = "";
    [ObservableProperty] private string certPasswordInput = "";
    [ObservableProperty] private string logSearch = "";
    [ObservableProperty] private int logCleanupDays = 7;
    [ObservableProperty] private bool confirmLogCleanupPending;
    [ObservableProperty] private bool confirmAllWriteGatesPending;

    public IReadOnlyList<int> LogCleanupOptions { get; } = new[] { 1, 3, 7, 14, 30 };
    public string CleanupButtonText => ConfirmLogCleanupPending
        ? $"确认删除 {LogCleanupDays} 天前日志"
        : "清理旧日志";
    public string WriteGatesButtonText => ConfirmAllWriteGatesPending
        ? "确认双帐套已核验并全选"
        : "已核验？集中勾选四项";

    partial void OnLogCleanupDaysChanged(int value)
    {
        ConfirmLogCleanupPending = false;
        OnPropertyChanged(nameof(CleanupButtonText));
    }

    partial void OnConfirmLogCleanupPendingChanged(bool value) =>
        OnPropertyChanged(nameof(CleanupButtonText));
    partial void OnConfirmAllWriteGatesPendingChanged(bool value) =>
        OnPropertyChanged(nameof(WriteGatesButtonText));
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

    /// <summary>Called only after the window has opened; keeps start failures visible in UI.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        if (_settingsInvalid)
        {
            await RefreshStatusAsync();
            return;
        }

        if (Settings.AutoStartApi)
        {
            if (string.IsNullOrWhiteSpace(Settings.SqlServer))
            {
                Message = "请先填写并保存 SQL Server 地址；配置完成后会自动启动 API。";
            }
            else
            {
                await StartAsync();
            }
        }
        await RefreshStatusAsync();
        await RefreshLogsAsync();
    }

    [RelayCommand]
    private void SelectAllWriteGates()
    {
        if (_settingsInvalid) { Message = "配置文件无效，禁止修改写入权限。"; return; }
        if (!ConfirmAllWriteGatesPending)
        {
            ConfirmAllWriteGatesPending = true;
            Message = "仅在 XTEST 与 COB 已分别完成 DBA 前检、幂等迁移和现场授权后，再点击一次确认勾选四项；不会自动保存。";
            return;
        }
        ConfirmAllWriteGatesPending = false;
        Settings.SchemaReviewed = true;
        Settings.EnableTestWrites = true;
        Settings.ProductionSchemaReviewed = true;
        Settings.EnableProductionWrites = true;
        OnPropertyChanged(nameof(Settings));
        Message = "已勾选四项（尚未生效），请填写经批准的 X3 审计用户编码并保存；更换数据库身份会重置授权。";
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
            Settings = _store.Load(); // refresh nested checkbox bindings after revoking old approvals
            ConfirmAllWriteGatesPending = false;
            SqlPasswordInput = "";
            CertPasswordInput = "";
            CredentialState = _vault.Read("sql-password") is null ? "未保存 SQL 密码" : "SQL 密码已保存";
            ApiUrl = "http://127.0.0.1:" + Settings.ApiPort;
            Message = "配置已保存至 EXE 同目录。数据库身份变化时写入授权已重置。";
            if (Settings.AutoStartApi && !string.IsNullOrWhiteSpace(Settings.SqlServer))
                await StartAsync();
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
        try
        {
            // Translate only for display, retaining the unmodified source in SQLite.
            // Searching Chinese labels also works within the most recent 500 raw records.
            var translated = _logs.Read(limit: 500).Select(LogLocalizer.ToChinese);
            var query = LogSearch?.Trim() ?? "";
            if (query.Length > 0)
                translated = translated.Where(row =>
                    row.Level.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (row.Exception?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            LogEntries = new ObservableCollection<ServerLog>(translated.Take(200));
        }
        catch (Exception ex) { Message = "日志读取失败：" + ex.Message; }
        finally { _refreshGate.Release(); }
    }

    [RelayCommand]
    private async Task DeleteOldLogsAsync()
    {
        if (!LogCleanupOptions.Contains(LogCleanupDays))
        {
            Message = "请选择 1、3、7、14 或 30 天，未执行删除。";
            return;
        }

        if (!ConfirmLogCleanupPending)
        {
            ConfirmLogCleanupPending = true;
            Message = $"将永久删除 {LogCleanupDays} 天前的 SQLite 历史日志。再点击一次“确认删除”才会执行；不会删除数据库及 WAL 文件。";
            return;
        }

        ConfirmLogCleanupPending = false;
        try
        {
            var days = LogCleanupDays;
            var removed = await Task.Run(() => _logs.DeleteOlderThan(days));
            Message = $"日志清理完成：删除了 {removed} 条 {days} 天前的记录。";
            await RefreshLogsAsync();
        }
        catch (Exception ex) { Message = "日志清理失败：" + ex.Message; }
    }

    public string GetDeviceKeyForClipboard() => _vault.GetOrCreateDeviceKey();

    public async Task CloseAsync()
    {
        try { await _runtime.DisposeAsync(); }
        finally { _refreshGate.Dispose(); }
    }
}
