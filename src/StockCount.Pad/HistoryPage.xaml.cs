using System.Collections.ObjectModel;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Pad.Services;

namespace StockCount.Pad;

/// <summary>Read-only, cursor-based history with a bounded 500-row UI window.</summary>
public partial class HistoryPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly InventoryCountDto _count;
    private readonly ObservableCollection<HistoryItemDisplay> _items = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    public sealed record HistoryItemDisplay(string BoxNumber, string LocationCode,
        string QuantityText, DateTime CreatedAt);
    private HistoryQuery? _query;
    private string? _cursor;
    private long? _total, _filtered;
    private long _previousRows;
    private bool _busy, _visible, _retryRequired, _loaded;
    private CancellationTokenSource? _request;

    public HistoryPage(StockCountApiClient api, InventoryCountDto count)
    {
        InitializeComponent();
        _api = api;
        _count = count;
        ItemsList.ItemsSource = _items;
        CountNumberText.Text = count.CountNumber;
        CountNameText.Text = count.Description;
        var production = api.EnvironmentName == "Production";
        EnvironmentLabel.Text = production ? "正式 · COB" : "测试 · XTEST";
        EnvironmentLabel.TextColor = Color.FromArgb(production ? "#9B5C11" : "#305CB6");
        EnvironmentBanner.BackgroundColor = Color.FromArgb(production ? "#FFF5E6" : "#EDF3FF");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!CountingInteractionPreferences.HistoryEnabled)
        {
            _visible = false;
            ItemsList.RemainingItemsThreshold = -1;
            _request?.Cancel();
            _items.Clear();
            _ids.Clear();
            _loaded = false;
            // Prevent a directly constructed page from displaying cached records or issuing a query.
            if (Content is not null) Content.IsVisible = false;
            if (Navigation.NavigationStack.LastOrDefault() == this)
                await Navigation.PopAsync();
            return;
        }
        if (Content is not null) Content.IsVisible = true;
        _visible = true;
        if (!_loaded) await ApplyFiltersAsync();
        else UpdateControls();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        ItemsList.RemainingItemsThreshold = -1;
        _request?.Cancel();
        base.OnDisappearing();
    }

    private async Task ApplyFiltersAsync()
    {
        if (!HistoryQuery.TryCreate(_api.EnvironmentName, _count.CountNumber,
            BoxSearchInput.Text, LocationFilterInput.Text, out var query))
        {
            await DisplayAlertAsync("筛选无效", "箱码最多20字符，库位最多10字符。", "OK");
            return;
        }
        BoxSearchInput.Unfocus();
        LocationFilterInput.Unfocus();
        _request?.Cancel();
        _query = query;
        _cursor = null;
        _total = _filtered = null;
        _previousRows = 0;
        ItemsList.RemainingItemsThreshold = -1;
        _items.Clear();
        _ids.Clear();
        TotalStatusText.Text = "已盘总记录：—";
        CountStatusText.Text = "正在读取";
        _retryRequired = false;
        _loaded = false;
        await LoadAsync(firstPage: true);
    }

    private async Task LoadAsync(bool firstPage = false, bool nextWindow = false)
    {
        if (!_visible || !CountingInteractionPreferences.HistoryEnabled || _query is null ||
            (!firstPage && (_busy || _cursor is null))) return;
        var query = _query;
        var cursor = firstPage ? null : _cursor;
        var request = new CancellationTokenSource();
        _request = request;
        _busy = true;
        _retryRequired = false;
        ItemsList.RemainingItemsThreshold = -1;
        LoadingIndicator.IsVisible = firstPage;
        LoadingIndicator.IsRunning = firstPage;
        EmptyPanel.IsVisible = false;
        FooterStatus.Text = firstPage ? "正在读取…" : "正在加载更多…";
        MoreButton.IsEnabled = false;
        try
        {
            var page = await _api.GetHistoryAsync(query.CountNumber, cursor, query.BoxSearch, query.Location, request.Token);
            if (!_visible || !CountingInteractionPreferences.HistoryEnabled ||
                !ReferenceEquals(_request, request) || request.IsCancellationRequested) return;
            if (page.Environment != query.Environment) throw new InvalidOperationException("查询帐套发生变化，请刷新。");
            if (page.Items.Count > HistoryPagingPolicy.PageSize || page.NextCursor is not null &&
                (page.Items.Count == 0 || page.NextCursor == cursor))
                throw new InvalidOperationException("服务端分页未推进，请刷新。");
            if (firstPage && (page.TotalCount is null || page.FilteredCount is null))
                throw new InvalidOperationException("服务端未返回记录总数，请升级后台。");
            if (nextWindow)
            {
                _previousRows += _items.Count;
                _items.Clear();
                _ids.Clear();
            }
            if (firstPage) { _total = page.TotalCount; _filtered = page.FilteredCount; }
            foreach (var item in page.Items)
                if (_ids.Add(item.DetailId))
                    _items.Add(new HistoryItemDisplay(item.BoxNumber, item.LocationCode,
                        CountQuantityDisplay.Format(item.QuantityStu, item.PackageUnit), item.CreatedAt));
            _cursor = page.NextCursor;
            _loaded = true;
            if (nextWindow && _items.Count > 0) ItemsList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            if (_items.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                EmptyText.Text = "没有符合条件的已盘记录";
            }
            UpdateStatus();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_visible || !ReferenceEquals(_request, request)) return;
            _retryRequired = true;
            FooterStatus.Text = "读取失败，请重试";
            if (_items.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                EmptyText.Text = "查询失败，请检查后台版本或网络";
            }
            await DisplayAlertAsync("查询已盘记录失败", error.Message, "OK");
        }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                _busy = false;
                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;
                UpdateControls();
            }
            request.Dispose();
        }
    }

    private void UpdateStatus()
    {
        TotalStatusText.Text = _total is long total ? $"已盘总记录：{total:N0} 条" : "已盘总记录：—";
        var end = _previousRows + _items.Count;
        CountStatusText.Text = $"已加载 {end:N0} / {_filtered.GetValueOrDefault():N0}";
        FooterStatus.Text = _items.Count == 0 ? "暂无记录" :
            $"当前 {_previousRows + 1:N0}–{end:N0} 条" + (_cursor is null ? " · 已到底" : "");
    }

    private void UpdateControls()
    {
        MoreButton.IsVisible = _cursor is not null || _retryRequired;
        MoreButton.IsEnabled = !_busy;
        MoreButton.Text = _retryRequired ? "重试" :
            HistoryPagingPolicy.NeedsNextWindow(_items.Count) ? "继续更早记录" : "加载更多";
        ItemsList.RemainingItemsThreshold = _visible && CountingInteractionPreferences.HistoryEnabled && HistoryPagingPolicy.CanAutoLoad(
            _items.Count, _cursor is not null, _busy, _retryRequired) ? 5 : -1;
    }

    private async void ThresholdReached(object sender, EventArgs e)
    {
        if (HistoryPagingPolicy.CanAutoLoad(_items.Count, _cursor is not null, _busy, _retryRequired))
            await LoadAsync();
    }

    private async void Back_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();
    private async void Refresh_Clicked(object sender, EventArgs e) => await ApplyFiltersAsync();
    private async void Filter_Clicked(object sender, EventArgs e) => await ApplyFiltersAsync();
    private async void ClearFilter_Clicked(object sender, EventArgs e)
    {
        BoxSearchInput.Text = "";
        LocationFilterInput.Text = "";
        await ApplyFiltersAsync();
    }
    private async void More_Clicked(object sender, EventArgs e)
    {
        if (_busy) return;
        if (!_loaded) { await ApplyFiltersAsync(); return; }
        await LoadAsync(nextWindow: HistoryPagingPolicy.NeedsNextWindow(_items.Count));
    }
}
