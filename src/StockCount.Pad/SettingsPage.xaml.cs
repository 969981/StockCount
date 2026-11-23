using StockCount.Pad.Services;

namespace StockCount.Pad;

public partial class SettingsPage : ContentPage
{
    private readonly StockCountApiClient _api;

    public SettingsPage(StockCountApiClient api)
    {
        InitializeComponent();
        _api=api;
        ApiAddress.Text=api.BaseUrl;
        EnvironmentPicker.SelectedIndex=api.EnvironmentName=="Production" ? 1 : 0;
    }

    private async void Save_Clicked(object sender,EventArgs e)
    {
        try
        {
            var mode=EnvironmentPicker.SelectedItem?.ToString()??"Test";
            await _api.ConfigureAsync(ApiAddress.Text??"",mode,DeviceKey.Text??"");
            DeviceKey.Text="";
            await DisplayAlertAsync("设置已保存","连接信息已保存在 PDA 本地。","OK");
        }
        catch(Exception error) { await DisplayAlertAsync("设置失败",error.Message,"OK"); }
    }

    private async void Test_Clicked(object sender,EventArgs e)
    {
        try
        {
            await _api.PingDatabaseAsync();
            await DisplayAlertAsync("测试连接","后端数据库连接成功。","OK");
        }
        catch(Exception error) { await DisplayAlertAsync("连接失败",error.Message,"OK"); }
    }
}
