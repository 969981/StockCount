using System.Globalization;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Pad.Services;

namespace StockCount.Pad;

/// <summary>Native MAUI view implementing the approved StockCount HTML interaction.</summary>
public partial class CountingPage : ContentPage
{
    private readonly StockCountApiClient _api;
    private readonly InventoryCountDto _selected;
    private readonly CountScanWorkflow _workflow;
    private readonly Queue<string> _scanQueue = new();
    private bool _busy;
    private bool _draining;
    private bool _submitting;
    private bool _restored;
    private bool _navigating;
    private int _toastVersion;
    private bool _pageVisible;

    public CountingPage(StockCountApiClient api, InventoryCountDto selected,
        IPendingSubmissionStore pendingStore)
    {
        InitializeComponent();
        _api = api;
        _selected = selected;
        _workflow = new CountScanWorkflow(api, selected.CountNumber,
            selected.Description, api.EnvironmentName, pendingStore);
        CountNumberText.Text = selected.CountNumber;
        CountNameText.Text = selected.Description;
        WarehouseText.Text = string.Join(" / ", new[] { selected.SiteCode, selected.WarehouseCode }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        PermissionText.Text = selected.CanSubmit ? "可提交" : "只读";
        PermissionText.TextColor = Color.FromArgb(selected.CanSubmit ? "#276C43" : "#627181");
        var production = api.EnvironmentName == "Production";
        EnvironmentLabel.Text = production ? "正式 · COB" : "测试 · XTEST";
        EnvironmentLabel.TextColor = Color.FromArgb(production ? "#9B5C11" : "#31537C");
        EnvironmentBanner.BackgroundColor = Color.FromArgb(production ? "#FFF5E6" : "#F0F4F9");
        RefreshScreen();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _pageVisible = true;
        ScanFeed.ScanReceived -= OnScannerReceived;
        ScanFeed.ScanReceived += OnScannerReceived;
        if (!_restored)
        {
            _restored = true;
            try
            {
                if (await _workflow.RestorePendingAsync())
                {
                    QuantityInput.Text = _workflow.ManualQuantity?.ToString(CultureInfo.InvariantCulture);
                    RefreshScreen();
                    await DisplayAlertAsync("恢复待确认提交",
                        "已恢复此前未确认的箱号、库位、数量和原请求 UUID。\n请先点击“检查提交结果”。",
                        "OK");
                }
            }
            catch (Exception error)
            {
                await DisplayAlertAsync("无法读取待确认草稿",
                    error.Message + "\n请勿重新生成盘点请求，先联系管理员检查设备存储。",
                    "OK");
            }
        }
    }

    protected override void OnDisappearing()
    {
        _pageVisible = false;
        ++_toastVersion;
        SuccessToast.IsVisible = false;
        _scanQueue.Clear();
        ScanFeed.ScanReceived -= OnScannerReceived;
        base.OnDisappearing();
    }

    private void OnScannerReceived(string raw)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_pageVisible) return;
            if ((_submitting && !_workflow.IsSuccessAwaitingDismiss) ||
                (!_submitting && _workflow.IsSuccessAwaitingDismiss))
            {
                ConnectionStateText.Text = "正在处理，请稍后重扫";
                return;
            }
            if (_scanQueue.Count >= 5)
            {
                ConnectionStateText.Text = "扫码过快，请重新扫描最后一箱";
                return;
            }
            _scanQueue.Enqueue(raw);
            // A confirmed commit may still be clearing its persisted draft.
            // Queue scans during that short cleanup, not during the toast.
            if (!_submitting) _ = DrainScanQueueAsync();
        });
    }

    private async Task DrainScanQueueAsync()
    {
        if (_draining) return;
        _draining = true;
        try
        {
            while (_scanQueue.Count > 0)
            {
                var raw = _scanQueue.Dequeue();
                await ProcessScanAsync(raw);
            }
        }
        finally
        {
            _draining = false;
            UpdateSubmitButton();
        }
    }

    private async Task ProcessScanAsync(string raw)
    {
        if (_busy) return;
        if (_workflow.HasUnresolvedSubmission)
        {
            await DisplayAlertAsync("提交结果待确认",
                "请先查询原 UUID 的提交结果；不能扫描下一箱覆盖当前草稿。", "OK");
            return;
        }

        // The current Entry may contain a draft not yet copied into Core.
        if (_workflow.Box is not null && !string.IsNullOrWhiteSpace(QuantityInput.Text))
        {
            var isSameQr = string.Equals(raw.Trim(),
                _workflow.OriginalQr, StringComparison.Ordinal);
            if (isSameQr)
            {
                ConnectionStateText.Text = "当前箱已读取，请直接提交";
                return;
            }
            var discard = await DisplayAlertAsync("当前箱尚未提交",
                "继续扫码会丢弃当前箱已输入的数量，是否确认？",
                "丢弃并继续", "继续当前箱");
            if (!discard) return;
            if (!_workflow.DiscardUnsubmittedDraft())
            {
                await DisplayAlertAsync("无法丢弃", "上次提交结果未确认。", "OK");
                return;
            }
            QuantityInput.Text = "";
        }

        _busy = true;
        ConnectionStateText.Text = "正在识别…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        var focusQuantity = false;
        try
        {
            var result = await _workflow.HandleScanAsync(raw);
            if (result.Kind is "BoxResolved" or "LocationUpdated" or "Duplicate")
                QuantityInput.Text = "";
            RefreshScreen();
            await ShowFeedbackAsync(result);
            focusQuantity = result.Kind == "BoxResolved";
        }
        catch (Exception error)
        {
            ConnectionStateText.Text = "连接异常";
            await DisplayAlertAsync("扫码失败", error.Message, "OK");
        }
        finally
        {
            _busy = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RefreshScreen();
            if (focusQuantity && _pageVisible && QuantityInput.IsEnabled)
                QuantityInput.Focus();
        }
    }

    private async void Submit_Clicked(object sender, EventArgs e)
    {
        if (_busy) return;
        if (!TryGetQuantity(out var quantity))
        {
            QuantityErrorText.Text = "箱内数量为必填项或格式无效！";
            QuantityErrorText.IsVisible = true;
            await DisplayAlertAsync("提示", "箱内数量为必填项！", "OK");
            return;
        }
        try { _workflow.EnterManualQuantity(quantity); }
        catch (InvalidOperationException error)
        {
            await DisplayAlertAsync("无法更改数量", error.Message, "OK");
            return;
        }
        await SubmitCurrentAsync();
    }

    private async void Retry_Clicked(object sender, EventArgs e) =>
        await SubmitCurrentAsync();

    private async Task SubmitCurrentAsync()
    {
        if (_busy || _draining) return;
        _busy = true;
        _submitting = true;
        ConnectionStateText.Text = "正在提交…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        try
        {
            var feedback = await _workflow.SubmitAsync();
            // Clear confirmed data immediately; the toast is independent.
            await ShowFeedbackAsync(feedback);
            if (feedback.Kind != "Submitted")
                RefreshScreen();
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("提交失败",
                error.Message + "\n请确认提交结果，避免重复扫描。", "OK");
        }
        finally
        {
            _busy = false;
            _submitting = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RefreshScreen();
            if (_pageVisible && !_workflow.HasUnresolvedSubmission &&
                !_workflow.IsSuccessAwaitingDismiss && _scanQueue.Count > 0)
                _ = DrainScanQueueAsync();
            else
                _scanQueue.Clear();
        }
    }

    private async void Check_Clicked(object sender, EventArgs e)
    {
        if (_busy || _draining) return;
        _busy = true;
        _submitting = true;
        ConnectionStateText.Text = "正在查询结果…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        try
        {
            var feedback = await _workflow.CheckPendingAsync();
            await ShowFeedbackAsync(feedback);
        }
        finally
        {
            _busy = false;
            _submitting = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RefreshScreen();
            if (_pageVisible && !_workflow.HasUnresolvedSubmission &&
                !_workflow.IsSuccessAwaitingDismiss && _scanQueue.Count > 0)
                _ = DrainScanQueueAsync();
            else
                _scanQueue.Clear();
        }
    }

    private async Task ShowFeedbackAsync(CountScanFeedback feedback)
    {
        if (feedback.Kind == "Submitted")
        {
            try
            {
                // Retire the confirmed request and clear the old box before
                // displaying any feedback. Do not await the toast lifetime.
                await _workflow.AcknowledgeSuccessAsync();
                QuantityInput.Text = "";
                QuantityInput.Unfocus();
                RefreshScreen();
                ShowSuccessToast(feedback.AutoDismissMs);
            }
            catch (Exception error)
            {
                await DisplayAlertAsync("提交已成功但本机收尾失败",
                    "服务器已确认保存。请勿重复提交。\n" + error.Message, "OK");
            }
            return;
        }

        if (feedback.Kind == "LocationUpdated")
        {
            ConnectionStateText.Text = "库位已更新";
            return;
        }
        if (feedback.Kind == "BoxResolved")
        {
            ConnectionStateText.Text = "箱码读取完成";
            return;
        }

        if (feedback.Kind == "Duplicate")
        {
            QuantityInput.Text = "";
            RefreshScreen();
        }
        if (!string.IsNullOrWhiteSpace(feedback.Message))
            await DisplayAlertAsync("提示", feedback.Message, "OK");
    }

    private void ShowSuccessToast(int durationMs)
    {
        if (!_pageVisible) return;
        var version = ++_toastVersion;
        SuccessToast.IsVisible = true;
        // A previous timer must not dismiss a more recent success toast.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(durationMs > 0
            ? durationMs : CountScanWorkflow.SuccessToastDurationMs), () =>
        {
            if (_pageVisible && version == _toastVersion)
                SuccessToast.IsVisible = false;
        });
    }

    private bool TryGetQuantity(out decimal quantity)
    {
        quantity = 0m;
        var value = QuantityInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var valid = decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.CurrentCulture, out quantity)
            || decimal.TryParse(value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out quantity);
        return valid && quantity >= 0m;
    }

    private void QuantityInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        QuantityErrorText.IsVisible = false;
        UpdateSubmitButton();
    }

    private void UpdateSubmitButton()
    {
        SubmitButton.IsEnabled = _selected.CanSubmit && !_busy && !_draining &&
            !_workflow.HasUnresolvedSubmission &&
            !_workflow.IsSuccessAwaitingDismiss &&
            _workflow.Box is not null && _workflow.Location is not null &&
            TryGetQuantity(out _);
    }

    private void RefreshScreen()
    {
        var located = !string.IsNullOrWhiteSpace(_workflow.Location);
        LocationText.Text = _workflow.Location ?? "—";
        LocationStatusText.Text = located ? "✓ 已确认" : "未扫描";
        LocationStatusText.TextColor = Color.FromArgb(located ? "#287348" : "#667687");
        LocationText.TextColor = Color.FromArgb(located ? "#154A87" : "#667687");
        LocationHintText.Text = located ? "重扫可切换" : "请扫描库位";
        BoxEmptyTitleText.Text = located ? "请扫描箱码" : "请先扫描库位";
        BoxEmptyHintText.Text = "使用 PDA 实体扫码键";

        var box = _workflow.Box;
        BoxFields.IsVisible = box is not null;
        BoxEmptyPanel.IsVisible = box is null;
        BoxStateText.Text = box is null ? "等待扫码" : "已读取";
        BoxStateText.TextColor = Color.FromArgb(box is null ? "#627181" : "#287348");
        BoxNumberText.Text = box?.BoxNumber ?? "";
        ItemCodeText.Text = box?.ItemCode ?? "";
        ItemNameText.Text = box?.ItemName ?? "";
        SpecificationText.Text = box?.Specification ?? "";
        LotText.Text = box?.LotNumber ?? "";
        StockUnitText.Text = box?.StockUnit ?? "";
        QuantityUnitText.Text = box?.StockUnit ?? "—";
        if (box is null && !_workflow.IsSuccessAwaitingDismiss)
            QuantityInput.Text = "";

        var pending = _workflow.HasUnresolvedSubmission;
        PendingPanel.IsVisible = pending;
        QuantityInput.IsEnabled = _selected.CanSubmit && box is not null &&
            !pending && !_busy && !_workflow.IsSuccessAwaitingDismiss;
        ReadOnlyWarning.IsVisible = !_selected.CanSubmit;
        ReadOnlyText.Text = _selected.CannotSubmitReason is null
            ? "当前盘点单暂不允许提交，请联系管理员。"
            : _selected.CannotSubmitReason switch
            {
                "ERP_WRITE_UNAVAILABLE" => "测试环境尚未启用写入，请在后台保存 XTEST 写入授权后重新进入盘点单。",
                "PRODUCTION_WRITE_DISABLED" => "正式环境尚未启用写入，请联系管理员保存 COB 写入授权。",
                "SCHEMA_NOT_APPROVED" => "当前帐套尚未完成结构核验，请联系管理员。",
                "COUNT_NOT_WRITABLE" => $"盘点单状态 {_selected.StatusCode} 不允许提交。",
                _ => $"当前盘点单暂不允许提交：{_selected.CannotSubmitReason}"
            };
        if (pending) ConnectionStateText.Text = "提交结果待确认";
        else if (!_busy && !_workflow.IsSuccessAwaitingDismiss)
            ConnectionStateText.Text = box is null
                ? (located ? "等待扫描箱码" : "等待扫描库位")
                : "等待人工数量";
        UpdateSubmitButton();
    }

    private async void History_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new HistoryPage(_api, _selected));

    private async void Back_Clicked(object sender, EventArgs e) =>
        await GoBackAsync();

    private async Task GoBackAsync()
    {
        if (_navigating || _busy) return;
        _navigating = true;
        try
        {
            if (_workflow.HasUnresolvedSubmission)
            {
                var leave = await DisplayAlertAsync("当前提交结果待确认",
                    "本地已保存原请求 UUID 和草稿，返回后可从盘点单列表恢复并查询结果。是否返回？",
                    "保留并返回", "继续检查");
                if (!leave) return;
            }
            else if (_workflow.HasUnsavedDraft)
            {
                var leave = await DisplayAlertAsync("当前箱尚未提交",
                    "返回列表将丢弃本次尚未提交的箱信息和数量，是否继续？",
                    "丢弃并返回", "继续盘点");
                if (!leave) return;
            }
            await Navigation.PopAsync();
        }
        finally { _navigating = false; }
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () => await GoBackAsync());
        return true;
    }
}
