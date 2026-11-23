using System.Collections.ObjectModel;
using StockCount.Contracts;
using StockCount.Pad.Services;

namespace StockCount.Pad;

public partial class CountListPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly ObservableCollection<InventoryCountDto> _counts=new();
    private string? _cursor;
    private bool _busy;

    public CountListPage(StockCountApiClient api)
    {
        InitializeComponent();
        _api=api;
        CountList.ItemsSource=_counts;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnvironmentLabel.Text = _api.EnvironmentName=="Test" ? "测试帐套 XTEST" : "正式帐套 COB";
        await LoadAsync(true);
    }

    private async Task LoadAsync(bool reset)
    {
        if (_busy) return;
        _busy=true;
        try
        {
            if (reset) { _cursor=null; _counts.Clear(); }
            var page=await _api.GetCountsAsync(_cursor);
            foreach(var item in page.Items.Where(x =>
                string.IsNullOrWhiteSpace(SearchInput.Text) ||
                x.CountNumber.Contains(SearchInput.Text,StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(SearchInput.Text,StringComparison.OrdinalIgnoreCase)))
                _counts.Add(item);
            _cursor=page.NextCursor;
            MoreButton.IsEnabled=_cursor is not null;
            StatusText.Text=$"已显示 {_counts.Count} 张";
        }
        catch(Exception error)
        {
            StatusText.Text="请求失败";
            await DisplayAlertAsync("无法查询盘点单",error.Message,"OK");
        }
        finally { _busy=false; }
    }

    private async void Refresh_Clicked(object sender,EventArgs e) => await LoadAsync(true);
    private async void More_Clicked(object sender,EventArgs e) => await LoadAsync(false);
    private async void OpenSettings_Clicked(object sender,EventArgs e) =>
        await Navigation.PushAsync(new SettingsPage(_api));

    private async void CountList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not InventoryCountDto chosen) return;
        CountList.SelectedItem=null;
        await Navigation.PushAsync(new CountingPage(_api,chosen));
    }
}
