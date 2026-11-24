using StockCount.Core;
using StockCount.Pad.Services;

namespace StockCount.Pad;

public partial class SettingsPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly IPendingSubmissionStore _pendingStore;
    private string _environment;

    public SettingsPage(StockCountApiClient api, IPendingSubmissionStore pendingStore)
    {
        InitializeComponent();
        _api = api;
        _pendingStore = pendingStore;
        _environment = api.EnvironmentName;
        ApiAddress.Text = api.BaseUrl;
        UpdateEnvironmentButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ScannerActionText.Text = "广播：" + ScanFeed.AndroidBroadcastAction;
        ScannerExtraText.Text = "字段：" + ScanFeed.AndroidBarcodeExtra;
        ScannerActionInput.Text = ScanFeed.AndroidBroadcastAction;
        ScannerExtraInput.Text = ScanFeed.AndroidBarcodeExtra;
        UpdateScannerDiagnostic(ScanFeed.LatestDiagnostic);
        ScanFeed.ScanObserved -= OnScanObserved;
        ScanFeed.ScanObserved += OnScanObserved;
        try
        {
            DeviceKeyStatus.Text = await _api.HasDeviceKeyAsync()
                ? "设备密钥已安全保存 · 留空表示不修改"
                : "未设置密钥（内网免密模式无需填写）";
        }
        catch
        {
            DeviceKeyStatus.Text = "无法读取设备密钥状态，请检查 Android 安全存储";
        }
    }

    protected override void OnDisappearing()
    {
        ScanFeed.ScanObserved -= OnScanObserved;
        base.OnDisappearing();
    }

    private void OnScanObserved(ScanFeed.ScanDiagnostic diagnostic)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateScannerDiagnostic(diagnostic));
    }

    private void UpdateScannerDiagnostic(ScanFeed.ScanDiagnostic? diagnostic)
    {
        ScannerStatusText.Text = diagnostic is null ? "等待硬件扫描" : "✓ 广播已收到";
        ScannerStatusText.TextColor = Color.FromArgb(
            diagnostic is null ? "#7F91AA" : "#19805B");
        LastScanText.Text = diagnostic is null ? "尚未收到扫码广播"
            : $"{diagnostic.ObservedAt:HH:mm:ss} · {diagnostic.Kind} · {diagnostic.Length} 字符";
    }

    private async void SaveScanner_Clicked(object sender, EventArgs e)
    {
        try
        {
            ScanFeed.Configure(ScannerActionInput.Text ?? "", ScannerExtraInput.Text ?? "");
            ScannerActionText.Text = "广播：" + ScanFeed.AndroidBroadcastAction;
            ScannerExtraText.Text = "字段：" + ScanFeed.AndroidBarcodeExtra;
            UpdateScannerDiagnostic(null);
            await DisplayAlertAsync("扫码参数已应用",
                "已重新注册硬件扫码广播。请在当前页面按 PDA 扫码键，观察下方接收状态。", "OK");
        }
        catch (ArgumentException error)
        {
            await DisplayAlertAsync("扫码参数无效", error.Message, "OK");
        }
    }

    private async void SelectProduction_Clicked(object sender, EventArgs e)
    {
        if (_environment == "Production") return;
        var confirmed = await DisplayAlertAsync(
            "切换到正式帐套",
            "正式 · COB 会读取正式盘点数据。后端启用写入后，提交操作将真实修改正式帐套。\n确定切换吗？",
            "确定切换", "取消");
        if (!confirmed) return;
        _environment = "Production";
        UpdateEnvironmentButtons();
    }

    private void SelectTest_Clicked(object sender, EventArgs e)
    {
        _environment = "Test";
        UpdateEnvironmentButtons();
    }

    private void UpdateEnvironmentButtons()
    {
        var production = _environment == "Production";
        TestEnvironmentButton.BackgroundColor =
            Color.FromArgb(production ? "#FFFFFF" : "#EDF3FF");
        TestEnvironmentButton.TextColor =
            Color.FromArgb(production ? "#5A6F8A" : "#305CB6");
        TestEnvironmentButton.BorderColor =
            Color.FromArgb(production ? "#DCE5F0" : "#9EBFFF");

        ProductionEnvironmentButton.BackgroundColor =
            Color.FromArgb(production ? "#FFF5E6" : "#FFFFFF");
        ProductionEnvironmentButton.TextColor =
            Color.FromArgb(production ? "#9B5C11" : "#5A6F8A");
        ProductionEnvironmentButton.BorderColor =
            Color.FromArgb(production ? "#E5B86E" : "#DCE5F0");
        ProductionWarning.IsVisible = production;
    }

    private async void Save_Clicked(object sender, EventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            if (!ApiEndpointPolicy.TryNormalize(ApiAddress.Text, out var nextUrl))
                throw new InvalidOperationException("请输入内网 IP:端口（例如 192.168.x.x:5080）或完整 HTTPS 地址。");
            var nextKey = DeviceKey.Text ?? "";
            var pending = await _pendingStore.LoadAsync(CancellationToken.None);
            if (pending is not null &&
                (_environment != pending.Environment ||
                 !string.Equals(nextUrl.TrimEnd('/'), _api.BaseUrl,
                     StringComparison.OrdinalIgnoreCase) ||
                 !string.IsNullOrWhiteSpace(nextKey)))
            {
                await DisplayAlertAsync("存在待确认提交",
                    "先恢复原帐套并确认提交结果，才可以更换 API 地址、帐套或设备密钥。\n当前草稿已安全保留。",
                    "OK");
                return;
            }

            await _api.ConfigureAsync(nextUrl, _environment, nextKey);
            ApiAddress.Text = _api.BaseUrl;
            DeviceKey.Text = "";
            DeviceKeyStatus.Text = await _api.HasDeviceKeyAsync()
                ? "设备密钥已安全保存 · 留空表示不修改"
                : "未设置密钥（内网免密模式无需填写）";
            await DisplayAlertAsync("设置已保存",
                "连接信息已保存到 PDA。请返回盘点单列表重新加载当前帐套。", "OK");
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("保存失败", error.Message, "OK");
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private void ToggleLegacyKey_Clicked(object sender, EventArgs e) =>
        LegacyKeyPanel.IsVisible = !LegacyKeyPanel.IsVisible;

    private async void Test_Clicked(object sender, EventArgs e)
    {
        ConnectionResult.Text = "正在检查 API 进程和数据库…";
        try
        {
            if (!string.Equals(_environment, _api.EnvironmentName, StringComparison.Ordinal) ||
                (!ApiEndpointPolicy.TryNormalize(ApiAddress.Text, out var testUrl) ||
                 !string.Equals(testUrl, _api.BaseUrl, StringComparison.OrdinalIgnoreCase)) ||
                !string.IsNullOrWhiteSpace(DeviceKey.Text))
            {
                ConnectionResult.Text = "请先保存设置，再测试当前连接。";
                return;
            }

            var apiOk = await _api.PingServerAsync();
            if (!apiOk)
            {
                ConnectionResult.Text = "API 不可达，请检查 IP、端口、局域网监听及 Windows 防火墙。";
                return;
            }
            try
            {
                var databaseOk = await _api.PingDatabaseAsync();
                ConnectionResult.Text = databaseOk
                    ? "✓ API 可达 · SQL Server SELECT 1 已就绪（不等于具备 ERP 写入权限）"
                    : "✓ API 可达 · 数据库未就绪";
            }
            catch (Exception error)
            {
                ConnectionResult.Text = "✓ API 可达 · /ready 未通过：" + error.Message;
            }
        }
        catch (Exception error)
        {
            ConnectionResult.Text = "连接异常：" + error.Message;
        }
    }

    private async void Back_Clicked(object sender, EventArgs e) =>
        await Navigation.PopAsync();
}
