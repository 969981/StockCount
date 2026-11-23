using System.Collections.ObjectModel;
using StockCount.Contracts;
using StockCount.Pad.Services;

namespace StockCount.Pad;

/// <summary>Read-only, server-backed record list, never derived from local fake data.</summary>
public partial class HistoryPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly InventoryCountDto _count;
    private readonly ObservableCollection<CountItemDto> _items = new();
    private string? _cursor;
    private bool _busy;

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
        if (_items.Count == 0) await LoadAsync(true);
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
            if (reset) { _items.Clear(); _cursor = null; }
            var page = await _api.GetItemsAsync(_count.CountNumber, _cursor);
            foreach (var item in page.Items)
                _items.Add(item);
            _cursor = page.NextCursor;
            MoreButton.IsVisible = _cursor is not null;
            CountStatusText.Text = $"共 {_items.Count} 条";
            if (_items.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                EmptyText.Text = "当前盘点单暂无已盘箱";
            }
        }
        catch (Exception error)
        {
            CountStatusText.Text = "读取失败";
            if (_items.Count == 0)
            {
                EmptyPanel.IsVisible = true;
                EmptyText.Text = "查询失败，请刷新重试";
            }
            await DisplayAlertAsync("查询已盘记录失败", error.Message, "OK");
        }
        finally
        {
            _busy = false;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private async void Back_Clicked(object sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void Refresh_Clicked(object sender, EventArgs e) =>
        await LoadAsync(true);

    private async void More_Clicked(object sender, EventArgs e) =>
        await LoadAsync(false);
}
