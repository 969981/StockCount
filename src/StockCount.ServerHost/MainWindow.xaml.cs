using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;

namespace StockCount.ServerHost;

public partial class MainWindow : Window
{
    private readonly ServerSettings _settings;
    private readonly SqliteLogStore _logs;
    private readonly ApiServerController _server = new();
    private readonly DispatcherTimer _timer;
    private bool _busy;
    private bool _closeAllowed;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _logs = new SqliteLogStore(SettingsStore.LogPath);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) =>
        {
            if (Tabs.SelectedIndex == 3) RefreshLogs();
            LogErrorText.Text = _logs.LastWriteError is not null
                ? "日志写入异常，请检查磁盘/权限"
                : _logs.DroppedCount > 0 ? $"队列已丢弃 {_logs.DroppedCount} 条日志" : "";
        };
        PopulateControls();
        LogDbPathText.Text = "SQLite: " + SettingsStore.LogPath;
        _logs.Add("Information", "ServerHost", "桌面服务管理器已启动。");
        RefreshLogs();
        RefreshStatus();
        _timer.Start();
    }

    private void PopulateControls()
    {
        SqlServerText.Text = _settings.SqlServer;
        SqlDatabaseText.Text = _settings.SqlDatabase;
        IntegratedSecurityCheck.IsChecked = _settings.IntegratedSecurity;
        SqlUserText.Text = _settings.SqlUser;
        SqlPasswordInput.Password = SettingsStore.Unprotect(_settings.SqlPasswordProtected);
        EncryptSqlCheck.IsChecked = _settings.EncryptSql;
        TrustSqlCertCheck.IsChecked = _settings.TrustServerCertificate;
        PortText.Text = _settings.ApiPort.ToString();
        AllowLanCheck.IsChecked = _settings.AllowLan;
        CertPathText.Text = _settings.CertificatePath;
        CertPasswordInput.Password = SettingsStore.Unprotect(_settings.CertificatePasswordProtected);
        UpdateAuthFields();
    }

    private void PersistControls()
    {
        if (_server.IsRunning || _busy)
            throw new InvalidOperationException("请先停止服务，再修改并保存配置。");
        if (!int.TryParse(PortText.Text.Trim(), out var port) || port is < 1024 or > 65535)
            throw new ArgumentException("API 端口应为 1024–65535。");
        if (string.IsNullOrWhiteSpace(SqlDatabaseText.Text))
            throw new ArgumentException("数据库名不能为空。");

        _settings.SqlServer = SqlServerText.Text.Trim();
        _settings.SqlDatabase = SqlDatabaseText.Text.Trim();
        _settings.IntegratedSecurity = IntegratedSecurityCheck.IsChecked == true;
        _settings.SqlUser = SqlUserText.Text.Trim();
        _settings.SqlPasswordProtected = SettingsStore.Protect(SqlPasswordInput.Password);
        _settings.EncryptSql = EncryptSqlCheck.IsChecked == true;
        _settings.TrustServerCertificate = TrustSqlCertCheck.IsChecked == true;
        _settings.ApiPort = port;
        _settings.AllowLan = AllowLanCheck.IsChecked == true;
        _settings.CertificatePath = CertPathText.Text.Trim();
        _settings.CertificatePasswordProtected = SettingsStore.Protect(CertPasswordInput.Password);
        SettingsStore.Save(_settings);
        _logs.Add("Information", "ServerHost", "服务及数据库配置已保存（凭据未写入日志）。");
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool running = _server.IsRunning;
        StartButton.IsEnabled = !running && !_busy;
        StopButton.IsEnabled = running && !_busy;
        DatabaseTab.IsEnabled = !running && !_busy;
        ServerSettingsTab.IsEnabled = !running && !_busy;
        ServiceStateText.Text = _busy ? "正在切换服务状态…" : running ? "运行中" : "已停止";
        StatusBadgeText.Text = running ? "● 运行中" : _busy ? "● 正在操作" : "● 未启动";
        StatusBadge.Background = new SolidColorBrush(running
            ? Color.FromRgb(224, 245, 234) : Color.FromRgb(234, 239, 247));
        StatusBadgeText.Foreground = new SolidColorBrush(running
            ? Color.FromRgb(17, 119, 66) : Color.FromRgb(82, 97, 115));
        ApiAddressText.Text = _settings.AllowLan
            ? $"https://<服务器内网地址>:{_settings.ApiPort}"
            : $"http://127.0.0.1:{_settings.ApiPort}";
        SqlSummaryText.Text = string.IsNullOrWhiteSpace(_settings.SqlServer)
            ? "SQL Server 尚未配置（需要现场填写）"
            : $"SQL Server：{_settings.SqlServer} / {_settings.SqlDatabase} · 生产写入禁用";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Save current values before entering the busy/start state.
            PersistControls();
            _busy = true;
            RefreshStatus();
            await _server.StartAsync(_settings, _logs);
            LastOperationText.Text = "服务启动成功。可访问 /health 检查进程状态。";
        }
        catch (Exception error)
        {
            _logs.Add("Error", "ServerHost", "启动服务失败。", error);
            LastOperationText.Text = "启动失败：" + error.Message;
            MessageBox.Show(this, error.Message, "无法启动服务",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            RefreshStatus();
            RefreshLogs();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_server.IsRunning || _busy) return;
        _busy = true;
        RefreshStatus();
        try
        {
            await _server.StopAsync(_logs);
            LastOperationText.Text = "API 服务已停止。";
        }
        catch (Exception error)
        {
            _logs.Add("Error", "ServerHost", "停止服务失败。", error);
            MessageBox.Show(this, error.Message, "停止失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            RefreshStatus();
            RefreshLogs();
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistControls();
            LastOperationText.Text = "配置已保存在本机；数据库密码和证书密码使用 Windows DPAPI 加密。";
            MessageBox.Show(this, "配置已保存到本机。", "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "配置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void TestDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            if (!_server.IsRunning) PersistControls();
            var connectionString = SettingsStore.BuildSqlConnectionString(_settings);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("请先填写 SQL Server 地址与数据库认证信息。");
            LastOperationText.Text = "正在测试数据库连接…";
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT 1;", connection);
            var value = await command.ExecuteScalarAsync();
            if (Convert.ToInt32(value) != 1)
                throw new InvalidOperationException("测试查询未返回预期结果。");
            _logs.Add("Information", "ServerHost", "数据库 SELECT 1 连接测试成功。");
            LastOperationText.Text = "数据库连接成功。";
            MessageBox.Show(this, "数据库连接成功（SELECT 1）。\n这并不代表全部业务表都已有访问权限。",
                "连接成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            _logs.Add("Error", "ServerHost", "数据库连接测试失败。", error);
            LastOperationText.Text = "数据库连接失败，请检查服务器、认证和 SQL Server 权限。";
            MessageBox.Show(this, error.Message, "数据库连接失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshLogs(); }
    }

    private void SqlAuth_Changed(object sender, RoutedEventArgs e) => UpdateAuthFields();

    private void UpdateAuthFields()
    {
        if (SqlUserText is null || SqlPasswordInput is null) return;
        var editable = IntegratedSecurityCheck.IsChecked != true;
        SqlUserText.IsEnabled = editable;
        SqlPasswordInput.IsEnabled = editable;
    }

    private void CopyDeviceKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = SettingsStore.Unprotect(_settings.DeviceKeyProtected);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException("当前没有设备接入密钥。");
            Clipboard.SetText(value);
            FooterText.Text = "设备接入密钥已复制；请仅配置到可信 PDA，勿公开传播。";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "复制失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(SettingsStore.LogPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e) => RefreshLogs();

    private void LogFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_logs is not null && LogGrid is not null) RefreshLogs();
    }

    private void RefreshLogs()
    {
        if (_logs is null || LogGrid is null) return;
        try { LogGrid.ItemsSource = _logs.Latest(250, LogFilterText.Text); }
        catch (Exception error) { LogErrorText.Text = "日志查询失败：" + error.Message; }
    }

    private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogDetailsText is null) return;
        LogDetailsText.Text = LogGrid.SelectedItem is LogRow row
            ? row.Message + (row.Exception is null ? "" : Environment.NewLine + row.Exception)
            : "";
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_closeAllowed) return;
        e.Cancel = true;
        _timer.Stop();
        try { await _server.StopAsync(_logs); }
        catch (Exception error) { _logs.Add("Error", "ServerHost", "退出时停止 API 失败。", error); }
        await _logs.DisposeAsync();
        _closeAllowed = true;
        Close();
    }
}
