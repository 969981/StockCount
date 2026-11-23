using System.Globalization;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Pad.Services;

namespace StockCount.Pad;

public partial class CountingPage : ContentPage
{
    private readonly CountScanWorkflow _workflow;
    private bool _busy;

    public CountingPage(StockCountApiClient api,InventoryCountDto selected)
    {
        InitializeComponent();
        _workflow=new CountScanWorkflow(api,selected.CountNumber,selected.Description);
        CountNumberText.Text=selected.CountNumber;
        CountNameText.Text=selected.Description;
        RefreshScreen();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScanFeed.ScanReceived+=OnScannerReceived;
    }

    protected override void OnDisappearing()
    {
        ScanFeed.ScanReceived-=OnScannerReceived;
        base.OnDisappearing();
    }

    private void OnScannerReceived(string code) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(code));

    private async void Scan_Clicked(object sender,EventArgs e)
    {
        var code=ScanInput.Text??"";
        ScanInput.Text="";
        await HandleScanAsync(code);
    }

    private async Task HandleScanAsync(string code)
    {
        if (_busy) return;
        _busy=true;
        try
        {
            var result=await _workflow.HandleScanAsync(code);
            RefreshScreen();
            await ShowFeedbackAsync(result);
        }
        catch (Exception error) { await DisplayAlertAsync("扫码失败",error.Message,"OK"); }
        finally { _busy=false; }
    }

    private async void Submit_Clicked(object sender,EventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(QuantityInput.Text))
        {
            await DisplayAlertAsync("提示","箱内数量为必填项！","OK");
            return;
        }
        if (!decimal.TryParse(QuantityInput.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var number) &&
            !decimal.TryParse(QuantityInput.Text,NumberStyles.Number,CultureInfo.InvariantCulture,out number))
        {
            await DisplayAlertAsync("提示","箱内数量格式无效。","OK");
            return;
        }

        _workflow.EnterManualQuantity(number);
        _busy=true;
        SubmitButton.IsEnabled=false;
        try
        {
            var feedback=await _workflow.SubmitAsync();
            RefreshScreen();
            await ShowFeedbackAsync(feedback);
        }
        finally { _busy=false; SubmitButton.IsEnabled=true; }
    }

    private async void Check_Clicked(object sender,EventArgs e)
    {
        if (_busy) return;
        _busy=true;
        try
        {
            var feedback=await _workflow.CheckPendingAsync();
            RefreshScreen();
            await ShowFeedbackAsync(feedback);
        }
        finally { _busy=false; }
    }

    private async Task ShowFeedbackAsync(CountScanFeedback feedback)
    {
        if (feedback.Kind=="Submitted")
        {
            SuccessBanner.IsVisible=true;
            await Task.Delay(feedback.AutoDismissMs==0?1000:feedback.AutoDismissMs);
            SuccessBanner.IsVisible=false;
            return;
        }
        if (!string.IsNullOrWhiteSpace(feedback.Message))
            await DisplayAlertAsync("提示",feedback.Message,"OK");
    }

    private void RefreshScreen()
    {
        LocationText.Text=_workflow.Location??"请扫描库位";
        var b=_workflow.Box;
        BoxNumberText.Text=b?.BoxNumber??"—";
        ItemCodeText.Text=b?.ItemCode??"—";
        ItemNameText.Text=b?.ItemName??"—";
        SpecificationText.Text=b?.Specification??"—";
        LotText.Text=b?.LotNumber??"—";
        StockUnitText.Text=b?.StockUnit??"—";
        QuantityUnitText.Text=b?.StockUnit??"STU";
        if (b is null) QuantityInput.Text="";
        CheckButton.IsVisible=_workflow.PendingRequestId.HasValue;
    }
}
