using System.Collections.ObjectModel;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Pad.Services;

namespace StockCount.Pad;

public partial class CountListPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly IPendingSubmissionStore _pendingStore;
    private readonly ObservableCollection<InventoryCountDto> _counts = new();
    private CancellationTokenSource? _searchDebounce;
    private string? _cursor;
    private Guid? _lastPromptedRequestId;
    private bool _busy;
    private string? _lastEnvironment;

    public CountListPage(StockCountApiClient api, IPendingSubmissionStore pendingStore)
    {
        InitializeComponent();
        _api = api;
        _pendingStore = pendingStore;
        CountList.ItemsSource = _counts;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _lastEnvironment = _api.EnvironmentName;
        RefreshEnvironment();
        await LoadAsync(true);

        try
        {
            PendingRecoveryButton.IsVisible =
                await _pendingStore.LoadAsync(CancellationToken.None) is not null;
        }
        catch { PendingRecoveryButton.IsVisible = true; }
        await OfferPendingRecoveryAsync();
    }

    private void RefreshEnvironment()
    {
        var production = _api.EnvironmentName == "Production";
        EnvironmentLabel.Text = production ? "正式 · COB" : "测试 · XTEST";
        EnvironmentLabel.TextColor = Color.FromArgb(production ? "#9B5C11" : "#305CB6");
        EnvironmentBanner.BackgroundColor = Color.FromArgb(production ? "#FFF5E6" : "#EDF3FF");
        EnvironmentBanner.Stroke = new SolidColorBrush(
            Color.FromArgb(production ? "#F3DEBA" : "#DCE8FF"));
    }

    private async Task LoadAsync(bool reset)
    {
        if (_busy) return;
        _busy = true;
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        EmptyPanel.IsVisible = false;
        try
        {
            if (reset)
            {
                _cursor = null;
                _counts.Clear();
            }
            StatusText.Text = "正在从服务器读取盘点单…";
            var page = await _api.GetCountsAsync(_cursor, SearchInput.Text);
            foreach (var item in page.Items)
                _counts.Add(item);
            _cursor = page.NextCursor;
            MoreButton.IsVisible = _cursor is not null;
            ConnectionStatusText.Text = "API 已连接";
            StatusText.Text = $"共显示 {_counts.Count} 张盘点单 · 不过滤状态";
            if (_counts.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                EmptyMessage.Text = "当前无匹配的盘点单";
            }
        }
        catch (Exception error)
        {
            ConnectionStatusText.Text = "连接异常";
            StatusText.Text = "无法获取盘点单，请检查网络";
            EmptyPanel.IsVisible = true;
            EmptyMessage.Text = "请求失败，可点击重新加载";
            if (reset && string.IsNullOrWhiteSpace(_api.BaseUrl))
                StatusText.Text = "请先在设置中配置后端 HTTPS 地址";
            else
                await DisplayAlertAsync("连接失败", error.Message, "OK");
        }
        finally
        {
            _busy = false;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private async void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounce = cts;
        try
        {
            await Task.Delay(360, cts.Token);
            if (!cts.IsCancellationRequested)
                await LoadAsync(true);
        }
        catch (OperationCanceledException) { }
    }

    private async void Refresh_Clicked(object sender, EventArgs e) =>
        await LoadAsync(true);

    private async void More_Clicked(object sender, EventArgs e) =>
        await LoadAsync(false);

    private async void OpenSettings_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new SettingsPage(_api, _pendingStore));

    private async void CountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not InventoryCountDto selected)
            return;
        CountList.SelectedItem = null;
        if (_busy) return;
        _busy = true;
        try
        {
            var details = await _api.GetCountDetailsAsync(selected.CountNumber);
            await Navigation.PushAsync(new CountingPage(_api, details.Count, _pendingStore));
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("无法读取盘点单", error.Message, "OK");
        }
        finally { _busy = false; }
    }

    private async void RecoverPending_Clicked(object sender, EventArgs e)
    {
        _lastPromptedRequestId = null;
        await OfferPendingRecoveryAsync();
    }

    private async Task OfferPendingRecoveryAsync()
    {
        try
        {
            var saved = await _pendingStore.LoadAsync(CancellationToken.None);
            if (saved is null || saved.RequestId == _lastPromptedRequestId)
                return;

            _lastPromptedRequestId = saved.RequestId;
            var resume = await DisplayAlertAsync("待确认盘点",
                $"发现一笔未确认的提交：{saved.CountNumber} / {saved.Box.BoxNumber}。\n" +
                $"原帐套：{(saved.Environment == "Production" ? "正式 COB" : "测试 XTEST")}。\n" +
                "恢复后会保留原 UUID 和实盘数量，先查询提交结果。",
                "恢复并检查", "稍后处理");

            if (!resume) return;
            if (saved.Environment != _api.EnvironmentName)
            {
                await _api.ConfigureAsync(_api.BaseUrl, saved.Environment, "");
                _lastEnvironment = null;
                RefreshEnvironment();
            }

            // Fetch fresh metadata from the actual environment. Do not fabricate a count.
            var details = await _api.GetCountDetailsAsync(saved.CountNumber);
            await Navigation.PushAsync(new CountingPage(_api, details.Count, _pendingStore));
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("恢复失败",
                error.Message + "\n本地待确认提交不会被自动删除。", "OK");
        }
    }
}
