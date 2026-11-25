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
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private bool _settingsInvalid;
    private bool _initialized;
    private bool _closing;
    private PortOwner? _externalApi;
    private PortOwner? _confirmedExternalApi;
    private int _externalPort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedWriteAuthorization))]
    private ServerSettings settings = new();
    public string SavedWriteAuthorization => $"写入策略：XTEST {(Settings.EnableTestWrites && Settings.SchemaReviewed ? "已启用" : "未启用")} · COB {(Settings.EnableProductionWrites && Settings.ProductionSchemaReviewed ? "已启用" : "未启用")}";
    [ObservableProperty] private ServerSettings locationDraft = new();
    [ObservableProperty] private string sqlPasswordInput = "";
    [ObservableProperty] private string certPasswordInput = "";
    [ObservableProperty] private string logSearch = "";
    [ObservableProperty] private int logCleanupDays = 7;
    [ObservableProperty] private bool confirmLogCleanupPending;

    public IReadOnlyList<int> LogCleanupOptions { get; } = new[] { 1, 3, 7, 14, 30 };
    public string CleanupButtonText => ConfirmLogCleanupPending
        ? $"确认删除 {LogCleanupDays} 天前日志"
        : "清理旧日志";

    partial void OnLogCleanupDaysChanged(int value)
    {
        ConfirmLogCleanupPending = false;
        OnPropertyChanged(nameof(CleanupButtonText));
    }

    partial void OnConfirmLogCleanupPendingChanged(bool value) =>
        OnPropertyChanged(nameof(CleanupButtonText));
    [ObservableProperty] private string message = "准备就绪。";
    [ObservableProperty] private string serviceState = "未启动";
    [ObservableProperty] private string portOwners = "尚未检查监听端口。";
    [ObservableProperty] private bool canStopExternal;
    [ObservableProperty] private string stopExternalText = "停止外部 API（需确认）";
    [ObservableProperty] private bool apiReachable;
    [ObservableProperty] private string databaseState = "未测试";
    [ObservableProperty] private string credentialState = "未检查";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SqlPasswordWatermark))]
    private bool hasSavedSqlPassword;

    // Display only a fixed placeholder, never the stored credential or its length.
    public string SqlPasswordWatermark => HasSavedSqlPassword ? "••••••••" : "请输入 SQL Server 密码";

    private void RefreshSqlCredentialState()
    {
        try
        {
            HasSavedSqlPassword = !string.IsNullOrEmpty(_vault.Read("sql-password"));
            CredentialState = HasSavedSqlPassword
                ? "SQL 密码已保存 · 输入新密码可替换，留空保留原密码"
                : "未保存 SQL 密码";
        }
        catch (Exception ex)
        {
            HasSavedSqlPassword = false;
            CredentialState = "凭据读取失败：" + ex.Message;
        }
    }
    [ObservableProperty] private string apiUrl = "http://127.0.0.1:5080";
    [ObservableProperty] private string apiDocsUrl = "http://127.0.0.1:5080/docs";
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
        LocationDraft = Settings.Copy();
        ApiUrl = "http://127.0.0.1:" + Settings.ApiPort;
        RefreshSqlCredentialState();
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
            if (!System.IO.File.Exists(_store.SettingsPath) || string.IsNullOrWhiteSpace(Settings.SqlServer))
            {
                Message = "请确认并保存数据库配置；配置完成后会自动启动 API。";
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
    private async Task SaveSettingsAsync()
    {
        if (_closing || !await _settingsSaveGate.WaitAsync(0)) return;
        try
        {
            if (_settingsInvalid) throw new InvalidOperationException("配置文件无效，请先修复或备份并移走原文件。");
            var old = _store.Load();
            var next = Settings.Copy();
            next.CopyLocationValidationFrom(old);
            var newSqlPassword = !string.IsNullOrEmpty(SqlPasswordInput);
            next.ApplyApprovedFieldWrites();
            next.X3AuditUser = next.EffectiveX3AuditUser;
            next.Validate();
            // Validate before stopping the currently running, owned API.
            var wasRunning = _runtime.IsRunning;
            if (wasRunning) await _runtime.StopAsync();
            if (newSqlPassword) _vault.Write("sql-password", SqlPasswordInput);
            if (!string.IsNullOrEmpty(CertPasswordInput)) _vault.Write("cert-password", CertPasswordInput);
            _store.Save(next);
            Settings = _store.Load();
            SqlPasswordInput = "";
            CertPasswordInput = "";
            RefreshSqlCredentialState();
            var savedMessage = "配置已保存；XTEST 与 COB 写入授权已启用。";
            Message = savedMessage;
            if ((wasRunning || Settings.AutoStartApi) && !string.IsNullOrWhiteSpace(Settings.SqlServer))
            {
                await StartAsync();
                if (_runtime.IsRunning) Message = savedMessage + " API 已启动。";
                else Message = savedMessage + " " + Message;
            }
        }
        catch (Exception ex) { Message = "保存失败：" + ex.Message; }
        finally { _settingsSaveGate.Release(); }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task SaveLocationSettingsAsync()
    {
        if (_closing || !await _settingsSaveGate.WaitAsync(0)) return;
        try
        {
            if (_settingsInvalid) throw new InvalidOperationException("配置文件无效，禁止修改。");
            if (!System.IO.File.Exists(_store.SettingsPath))
                throw new InvalidOperationException("请先保存数据库配置，再单独保存盘点校验规则。");
            var next = _store.Load();
            next.CopyLocationValidationFrom(LocationDraft);
            next.Validate();
            var wasRunning = _runtime.IsRunning;
            if (wasRunning) await _runtime.StopAsync();
            _store.Save(next);
            Settings.CopyLocationValidationFrom(next);
            LocationDraft = next.Copy();
            Message = "盘点校验规则已保存，下次启动 API 生效；外部 API 不会自动重启。";
            if (wasRunning)
            {
                await _runtime.StartAsync(next);
                Message = "盘点校验规则已保存，API 已重新启动。";
            }
        }
        catch (Exception ex) { Message = "保存盘点校验规则或重启失败：" + ex.Message; }
        finally { _settingsSaveGate.Release(); }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_closing) return;
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
        if (!_runtime.IsRunning)
        {
            Message = "当前管理器未启动 API。外部服务请先确认身份，再使用专用停止按钮。";
            await RefreshStatusAsync();
            return;
        }
        try { await _runtime.StopAsync(); Message = "API 已停止。"; }
        catch (Exception ex) { Message = "停止失败：" + ex.Message; }
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task StopExternalAsync()
    {
        var candidate = _externalApi;
        if (candidate is null || !CanStopExternal)
        {
            Message = "无法确认外部 API 身份，不允许终止进程。";
            return;
        }
        if (_confirmedExternalApi != candidate)
        {
            _confirmedExternalApi = candidate;
            StopExternalText = $"确认终止 PID {candidate.ProcessId}";
            Message = "请确认其他管理器及 PDA 已停止操作、没有待提交盘点事务。旧 API 无管理令牌，停止将直接终止该进程；再次点击才执行。";
            return;
        }
        _confirmedExternalApi = null;
        StopExternalText = "停止外部 API（需确认）";
        try
        {
            await _runtime.StopExternalAsync(_externalPort, candidate);
            Message = "外部 API 已停止，端口已释放。现在可以启动新版 API。";
        }
        catch (Exception ex) { Message = "停止外部 API 失败：" + ex.Message; }
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
    private void OpenApiDocs()
    {
        try
        {
            if (!ApiReachable)
            {
                Message = "请先启动或确认 API 正常运行，再打开中文接口文档。";
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                ApiDocsUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Message = "打开接口文档失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        if (_closing) return;
        try
        {
            var current = _store.Load();
            var status = await _runtime.StatusAsync(current);
            ServiceState = status.State switch
            {
                "OwnedHealthy" => "运行中 · 当前管理器 · 健康",
                "OwnedUnhealthy" => "运行中 · 当前管理器 · 未就绪",
                "ExternalApi" => "外部 API 正在运行（可能为残留进程）",
                "PortConflict" => "端口占用 · 服务身份未确认",
                _ => "已停止"
            };
            ApiReachable = status.Healthy;
            PortOwners = status.Owners.Count == 0 ? $"端口 {status.ApiPort} / {status.ApiPort + 1} 无监听。" :
                string.Join("\n", status.Owners.Select(x => x.Display));
            _externalApi = status.ExternalApi;
            _externalPort = status.ApiPort;
            CanStopExternal = _externalApi is not null;
            if (_confirmedExternalApi != _externalApi)
            {
                _confirmedExternalApi = null;
                StopExternalText = "停止外部 API（需确认）";
            }
            ApiDocsUrl = $"http://127.0.0.1:{status.ApiPort}/docs";
            ApiUrl = (current.AllowLan ? "http://<服务器局域网IP>:" : "http://127.0.0.1:") + current.ApiPort;
            if (status.LastError is not null && status.State == "Stopped") Message = status.LastError;
            OnPropertyChanged(nameof(IsRunning));
        }
        catch (Exception ex) { ServiceState = "状态未知 · 检测失败"; PortOwners = ex.Message; ApiReachable = false; CanStopExternal = false; _externalApi = null; _confirmedExternalApi = null; StopExternalText = "停止外部 API（需确认）"; Message = "状态检查失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (_closing || !await _refreshGate.WaitAsync(0)) return;
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
        _closing = true;
        await _runtime.DisposeAsync();
    }
}
