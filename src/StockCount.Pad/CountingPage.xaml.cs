using System.Globalization;
using System.Diagnostics;
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
    private readonly IScanFeedbackService _feedback;
    private readonly Queue<(string Raw, long ReceivedAt)> _scanQueue = new();
    private static readonly Color ConfirmedGreen = Color.FromArgb("#287348");
    private static readonly Color MutedGray = Color.FromArgb("#667687");
    private static readonly Color LocationBlue = Color.FromArgb("#154A87");
    private static readonly Color BoxGray = Color.FromArgb("#627181");
    private bool _rendered;
    private string? _renderedLocation;
    private BoxScanData? _renderedBox;
    private bool _busy;
    private bool _draining;
    private bool _submitting;
    private bool _restored;
    private bool _navigating;
    private readonly SuccessToastLifetime _toastLifetime = new();
    private bool _pageVisible;
    private readonly CountingScrollIntent _detailsScroll = new();
    private long _scrollSchedule;
    private bool? _inlineActions;

    public CountingPage(StockCountApiClient api, InventoryCountDto selected,
        IPendingSubmissionStore pendingStore, IScanFeedbackService feedback)
    {
        InitializeComponent();
        _api = api;
        _feedback = feedback;
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
        RenderReadOnlyPermission();
        RefreshScreen();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _pageVisible = true;
        HistoryButton.IsVisible = CountingInteractionPreferences.HistoryEnabled;
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
        CancelDetailsScroll();
        DismissBusinessToast();
        _scanQueue.Clear();
        ScanFeed.ScanReceived -= OnScannerReceived;
        base.OnDisappearing();
    }

    private void OnScannerReceived(string raw)
    {
        var receivedAt = Stopwatch.GetTimestamp();
        void Enqueue()
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
                Notify(FeedbackReason.QueueFull);
                ShowBusinessToast("扫码过快，请重新扫描最后一箱", false);
                return;
            }
            _scanQueue.Enqueue((raw, receivedAt));
            Notify(FeedbackReason.ReadAccepted);
            // A confirmed commit may still be clearing its persisted draft.
            // Queue scans during that short cleanup, not during the toast.
            if (!_submitting) _ = DrainScanQueueAsync();
        }
        if (MainThread.IsMainThread) Enqueue();
        else MainThread.BeginInvokeOnMainThread(Enqueue);
    }

    private async Task DrainScanQueueAsync()
    {
        if (_draining) return;
        _draining = true;
        try
        {
            while (_scanQueue.Count > 0)
            {
                var (raw, receivedAt) = _scanQueue.Dequeue();
                ClientPerformance.Shared.RecordSince(ClientPerfStage.ScanQueue, receivedAt);
                try { await ProcessScanAsync(raw); }
                finally { ClientPerformance.Shared.RecordSince(ClientPerfStage.ScanTotal, receivedAt); }
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
            Notify(FeedbackReason.PendingBlocked);
            DismissBusinessToast();
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
            DismissBusinessToast();
            var discard = await DisplayAlertAsync("当前箱尚未提交",
                "继续扫码会丢弃当前箱已输入的数量，是否确认？",
                "丢弃并继续", "继续当前箱");
            if (!discard) return;
            if (!_workflow.DiscardUnsubmittedDraft())
            {
                Notify(FeedbackReason.PendingBlocked);
                await DisplayAlertAsync("无法丢弃", "上次提交结果未确认。", "OK");
                return;
            }
            QuantityInput.Text = "";
        }

        CancelDetailsScroll();
        _busy = true;
        ConnectionStateText.Text = "正在识别…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        var focusQuantity = false;
        try
        {
            CountScanFeedback result;
            using (ClientPerformance.Shared.Measure(ClientPerfStage.ScanWorkflow))
                result = await _workflow.HandleScanAsync(raw);
            if (result.Kind is "BoxResolved" or "LocationUpdated" or "Duplicate")
                QuantityInput.Text = "";
            RefreshScreen();
            await ShowFeedbackAsync(result);
            focusQuantity = result.Kind == "BoxResolved";
        }
        catch (Exception error)
        {
            Notify(FeedbackReason.ScanException);
            ConnectionStateText.Text = "连接异常";
            ShowBusinessToast("扫码失败：" + error.Message, false);
        }
        finally
        {
            _busy = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RefreshScreen();
            if (focusQuantity && _pageVisible && QuantityInput.IsEnabled)
            {
                using var timing = ClientPerformance.Shared.Measure(ClientPerfStage.QuantityFocus);
                QuantityInput.Focus();
                RequestDetailsScroll(CountingScrollTarget.Bottom);
            }
            else if (_pageVisible && _workflow.Box is null && !_workflow.HasUnresolvedSubmission)
                QuantityInput.Unfocus();
        }
    }

    private async void Submit_Clicked(object sender, EventArgs e)
    {
        if (_busy) return;
        if (!TryGetQuantity(out var quantity))
        {
            Notify(FeedbackReason.QuantityInvalid);
            QuantityErrorText.Text = "箱内数量为必填项或格式无效！";
            QuantityErrorText.IsVisible = true;
            ShowBusinessToast("箱内数量为必填项或格式无效！", false);
            return;
        }
        try { _workflow.EnterManualQuantity(quantity); }
        catch (InvalidOperationException error)
        {
            Notify(FeedbackReason.QuantityInvalid);
            if (_workflow.HasUnresolvedSubmission)
                await DisplayAlertAsync("无法更改数量", error.Message, "OK");
            else
                ShowBusinessToast(error.Message, false);
            return;
        }
        await SubmitCurrentAsync();
    }

    private async void Retry_Clicked(object sender, EventArgs e) =>
        await SubmitCurrentAsync(recovery: true);

    private async Task SubmitCurrentAsync(bool recovery = false)
    {
        if (_busy || _draining) return;
        CancelDetailsScroll();
        using var total = ClientPerformance.Shared.Measure(ClientPerfStage.SubmitTotal);
        _busy = true;
        _submitting = true;
        ConnectionStateText.Text = "正在提交…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        try
        {
            CountScanFeedback feedback;
            using (ClientPerformance.Shared.Measure(ClientPerfStage.SubmitWorkflow))
                feedback = await _workflow.SubmitAsync();
            // Clear confirmed data immediately; the toast is independent.
            await ShowFeedbackAsync(feedback, submission: true, recovery: recovery);
            if (feedback.Kind != "Submitted")
                RefreshScreen();
        }
        catch (Exception error)
        {
            Notify(FeedbackReason.SubmissionException);
            DismissBusinessToast();
            await DisplayAlertAsync("提交结果需确认",
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
        CancelDetailsScroll();
        using var total = ClientPerformance.Shared.Measure(ClientPerfStage.CheckTotal);
        _busy = true;
        _submitting = true;
        ConnectionStateText.Text = "正在查询结果…";
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RefreshScreen();
        try
        {
            var feedback = await _workflow.CheckPendingAsync();
            await ShowFeedbackAsync(feedback, submission: true, recovery: true);
        }
        catch (Exception error)
        {
            Notify(FeedbackReason.SubmissionException);
            DismissBusinessToast();
            await DisplayAlertAsync("查询提交结果异常", error.Message + "\n请保留原请求并再次确认。", "OK");
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

    private void Notify(FeedbackReason reason, Guid? requestId = null)
    {
        if (_pageVisible) _feedback.Notify(new FeedbackEvent(reason, requestId));
    }

    private async Task ShowFeedbackAsync(CountScanFeedback feedback, bool submission = false, bool recovery = false)
    {
        if (feedback.Kind == "Submitted")
        {
            var requestId = _workflow.PendingRequestId;
            try
            {
                // Retire the confirmed request and clear the old box before
                // displaying any feedback. Do not await the toast lifetime.
                using (ClientPerformance.Shared.Measure(ClientPerfStage.ConfirmCleanup))
                    await _workflow.AcknowledgeSuccessAsync();
                QuantityInput.Text = "";
                QuantityInput.Unfocus();
                RefreshScreen();
                RequestDetailsScroll(CountingScrollTarget.Top);
                ShowBusinessToast(feedback.Message ?? "提交成功！", true);
                Notify(FeedbackPolicy.FromWorkflow(feedback.Kind, submission, recovery), requestId);
            }
            catch (Exception error)
            {
                Notify(FeedbackReason.ConfirmedCleanupFailed, requestId);
                DismissBusinessToast();
                await DisplayAlertAsync("提交已成功但本机收尾失败",
                    "服务器已确认保存。请勿重复提交。\n" + error.Message, "OK");
            }
            return;
        }

        Notify(FeedbackPolicy.FromWorkflow(feedback.Kind, submission, recovery), _workflow.PendingRequestId);
        if (feedback.Kind == "LocationUpdated")
        {
            ClearFailureToast();
            ConnectionStateText.Text = "库位已更新";
            QuantityInput.Unfocus();
            RequestDetailsScroll(CountingScrollTarget.Top);
            return;
        }
        if (feedback.Kind == "BoxResolved")
        {
            ClearFailureToast();
            ConnectionStateText.Text = _workflow.IsUpdate ? "等待更新数量" : "箱码读取完成";
            return;
        }

        if (feedback.Kind == "Duplicate" || feedback.ErrorCode is ("BOX_ALREADY_RECORDED" or "UPDATE_VERSION_CONFLICT" or "UPDATE_RECORD_CONFLICT"))
        {
            QuantityInput.Text = "";
            RefreshScreen();
        }
        if (string.IsNullOrWhiteSpace(feedback.Message)) return;
        if (BusinessToastPolicy.IsAutomaticFailure(feedback, submission, _workflow.HasUnresolvedSubmission))
        {
            ShowBusinessToast(feedback.Message, false);
            if (_workflow.Conflict is not null) RequestDetailsScroll(CountingScrollTarget.Top);
            return;
        }
        DismissBusinessToast();
        await DisplayAlertAsync("提示", feedback.Message, "OK");
    }

    private bool _toastIsFailure;

    private void ClearFailureToast()
    {
        if (_toastIsFailure) DismissBusinessToast();
    }

    private void DismissBusinessToast()
    {
        _toastLifetime.Invalidate();
        BusinessToast.IsVisible = false;
        _toastIsFailure = false;
    }

    private void ShowBusinessToast(string message, bool success)
    {
        if (!_pageVisible) return;
        var version = _toastLifetime.Begin();
        var durationMs = CountingInteractionPreferences.SuccessToastDurationMs;
        _toastIsFailure = !success;
        BusinessToast.BackgroundColor = Color.FromArgb(success ? "#287348" : "#A93232");
        BusinessToastTitle.Text = success ? "✓ 盘点成功" : "! 扫码 / 业务提示";
        BusinessToastMessage.Text = message;
        BusinessToast.IsVisible = true;
        // No await or queue lock for the presentation lifetime.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(durationMs), () =>
        {
            if (_pageVisible && _toastLifetime.IsCurrent(version))
                DismissBusinessToast();
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

    // Scope native styling to this quantity Entry. Preserve the original numeric
    // keyboard, cursor, text events, font scaling and Android text metrics.
    private void QuantityInput_HandlerChanged(object sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry entry &&
            entry.Handler?.PlatformView is global::Android.Widget.EditText native)
        {
            native.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(
                global::Android.Graphics.Color.Transparent);
            native.SetIncludeFontPadding(true);
            var density = native.Resources?.DisplayMetrics?.Density ?? 1f;
            var vertical = (int)Math.Round(4 * density);
            native.SetPadding(0, vertical, 0, vertical);
        }
#endif
    }

    private void QuantityInput_FocusChanged(object sender, FocusEventArgs e)
    {
        QuantityFrame.Stroke = new SolidColorBrush(Color.FromArgb(e.IsFocused ? (_workflow.IsUpdate ? "#B77920" : "#175AB7") : "#8799AA"));
        if (!e.IsFocused && _detailsScroll.Target == CountingScrollTarget.Bottom)
            CancelDetailsScroll();
    }

    // UI only: coalesce content/IME resize events, never hold the scan queue.
    private void RequestDetailsScroll(CountingScrollTarget target)
    {
        if (!_pageVisible) return;
        _detailsScroll.Begin(target, Environment.TickCount64);
        ScheduleDetailsScroll();
    }

    private void CancelDetailsScroll()
    {
        _detailsScroll.Cancel();
        ++_scrollSchedule;
    }

    private void DetailsViewport_SizeChanged(object sender, EventArgs e) => ScheduleDetailsScroll();

    private void ScheduleDetailsScroll()
    {
        if (!_pageVisible || !_detailsScroll.CanApply(_detailsScroll.Version, Environment.TickCount64)) return;
        var version = _detailsScroll.Version;
        var scheduled = ++_scrollSchedule;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), async () =>
        {
            if (!_pageVisible || scheduled != _scrollSchedule ||
                !_detailsScroll.CanApply(version, Environment.TickCount64)) return;
            var bottom = _detailsScroll.Target == CountingScrollTarget.Bottom;
            if (bottom && (_workflow.Box is null || _workflow.HasUnresolvedSubmission || _submitting)) return;
            if (CountDetailsScroll.Height <= 0) return;
            var y = bottom ? Math.Max(0, CountDetailsScroll.ContentSize.Height - CountDetailsScroll.Height) : 0;
            try { await CountDetailsScroll.ScrollToAsync(0, y, false); }
            catch (Exception) { /* Layout/lifecycle change must not become a business failure. */ }
        });
    }

    private void QuantityActionBar_SizeChanged(object sender, EventArgs e)
    {
        if (QuantityActionBar is null || QuantityFrame is null || SubmitAction is null ||
            QuantityActionBar.Width <= 0) return;
        var inline = CountingLayoutPolicy.UseInlineActions(QuantityActionBar.Width);
        if (_inlineActions == inline) return;
        _inlineActions = inline;
        Grid.SetColumnSpan(QuantityFrame, inline ? 1 : 2);
        Grid.SetRow(SubmitAction, inline ? 1 : 3);
        Grid.SetColumn(SubmitAction, inline ? 1 : 0);
        Grid.SetColumnSpan(SubmitAction, inline ? 1 : 2);
        SubmitAction.Margin = inline ? new Thickness(6, 0, 0, 0) : new Thickness(0, 4, 0, 0);
        SubmitAction.MinimumWidthRequest = inline ? 94 : 0;
    }

    private void ClearQuantity_Clicked(object sender, EventArgs e)
    {
        if (!QuantityInput.IsEnabled || string.IsNullOrEmpty(QuantityInput.Text)) return;
        QuantityInput.Text = "";
        QuantityInput.Focus();
    }

    private void QuantityInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuantityErrorText.IsVisible) QuantityErrorText.IsVisible = false;
        UpdateSubmitButton();
    }

    private void UpdateSubmitButton()
    {
        var enabled = _selected.CanSubmit && !_busy && !_draining &&
            !_workflow.HasUnresolvedSubmission &&
            !_workflow.IsSuccessAwaitingDismiss &&
            _workflow.Box is not null && _workflow.Location is not null &&
            TryGetQuantity(out _);
        if (SubmitButton.IsEnabled != enabled) SubmitButton.IsEnabled = enabled;
        SubmitButton.Text = _workflow.Conflict is not null ? "禁止提交" : _workflow.IsUpdate ? "更新盘点" : "提交盘点";
        SubmitButton.BackgroundColor = Color.FromArgb(enabled ? (_workflow.IsUpdate ? "#B77920" : "#175AB7") : "#DCE3EC");
        SubmitButton.TextColor = Color.FromArgb(enabled ? "#FFFFFF" : "#8290A1");
        var canClear = QuantityInput.IsEnabled && !string.IsNullOrEmpty(QuantityInput.Text);
        if (ClearQuantityButton.IsEnabled != canClear) ClearQuantityButton.IsEnabled = canClear;
    }

    // Compare the last rendered domain state before touching native controls.
    // Only the dynamic busy/pending/input state is evaluated on each pass.
    private void RefreshScreen()
    {
        using var timing = ClientPerformance.Shared.Measure(ClientPerfStage.UiRefresh);
        var location = _workflow.Location;
        var located = !string.IsNullOrWhiteSpace(location);
        if (!_rendered || !string.Equals(location, _renderedLocation, StringComparison.Ordinal))
        {
            LocationText.Text = location ?? "—";
            LocationStatusText.Text = located ? "✓ 已确认" : "未扫描";
            LocationStatusText.TextColor = located ? ConfirmedGreen : MutedGray;
            LocationText.TextColor = located ? LocationBlue : MutedGray;
            LocationHintText.Text = located ? "重扫可切换" : "请扫描库位";
            BoxEmptyTitleText.Text = located ? "请扫描箱码" : "请先扫描库位";
            _renderedLocation = location;
        }
        var box = _workflow.Box;
        if (!_rendered || !Equals(box, _renderedBox))
        {
            BoxFields.IsVisible = box is not null;
            BoxEmptyPanel.IsVisible = box is null;
            BoxStateText.Text = box is null ? "等待扫码" : "已读取";
            BoxStateText.TextColor = box is null ? BoxGray : ConfirmedGreen;
            BoxNumberText.Text = box?.BoxNumber ?? "";
            ItemCodeText.Text = box?.ItemCode ?? "";
            ItemNameText.Text = box?.ItemName ?? "";
            SpecificationText.Text = box?.Specification ?? "";
            LotText.Text = box?.LotNumber ?? "";
            StockUnitText.Text = box?.StockUnit ?? "";
            QuantityUnitText.Text = box?.StockUnit ?? "—";
            _renderedBox = box;
        }
        var conflict = _workflow.Conflict;
        var update = _workflow.IsUpdate;
        ConflictCard.IsVisible = conflict is not null;
        BoxEmptyPanel.IsVisible = box is null && conflict is null;
        BoxStateText.Text = conflict is not null ? "禁止提交" : update ? "已盘点 · 更新模式" : box is null ? "等待扫码" : "未盘点 · 新增模式";
        BoxStateText.TextColor = conflict is not null ? Color.FromArgb("#A93232") :
            update ? Color.FromArgb("#A46A15") : box is null ? BoxGray : ConfirmedGreen;
        if (conflict is not null)
        {
            ConflictTitle.Text = conflict.Message ?? "原记录不可更新";
            ConflictDetails.Text = $"箱码：{conflict.BoxNumber ?? "—"}\n原盘点单：{conflict.CountNumber ?? "—"}\n原库位：{conflict.LocationCode ?? "—"}\n当前库位：{location ?? "—"}" +
                (conflict.OldQuantity is decimal old ? $"\n原数量：{CountQuantityDisplay.Format(old,conflict.StockUnit ?? "")}" : "");
            if (conflict.UpdatedAt is DateTime time) ConflictDetails.Text += $"\n原修改时间：{time:yyyy-MM-dd HH:mm}";
        }
        QuantityCaption.Text = update ? "更新后数量 *" : "箱内数量 *";
        QuantityInput.Placeholder = update ? "新数量" : "输入数量";
        OriginalQuantityText.IsVisible = update;
        if (update && box?.Correction?.OldQuantity is decimal previous)
            OriginalQuantityText.Text = "原数量：" + CountQuantityDisplay.Format(previous,box.StockUnit);
        _rendered = true;
        if (box is null && !_workflow.IsSuccessAwaitingDismiss &&
            !string.IsNullOrEmpty(QuantityInput.Text))
            QuantityInput.Text = "";

        var pending = _workflow.HasUnresolvedSubmission;
        if (PendingPanel.IsVisible != pending) PendingPanel.IsVisible = pending;
        var enabled = _selected.CanSubmit && box is not null &&
            !pending && !_busy && !_workflow.IsSuccessAwaitingDismiss;
        if (QuantityInput.IsEnabled != enabled) QuantityInput.IsEnabled = enabled;
        string? status = pending ? "提交结果待确认" :
            !_busy && !_workflow.IsSuccessAwaitingDismiss
                ? (conflict is not null ? "原记录冲突" : box is null ? (located ? "等待扫描箱码" : "等待扫描库位") : update ? "等待更新数量" : "等待人工数量")
                : null;
        if (status is not null && ConnectionStateText.Text != status)
            ConnectionStateText.Text = status;
        UpdateSubmitButton();
    }

    private void RenderReadOnlyPermission()
    {
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
    }

    private async void History_Clicked(object sender, EventArgs e)
    {
        // Recheck the preference at navigation time, not only when rendering the button.
        var enabled = CountingInteractionPreferences.HistoryEnabled;
        HistoryButton.IsVisible = enabled;
        if (!HistoryVisibilityPolicy.CanNavigate(enabled, _pageVisible,
            _busy || _draining || _submitting, _navigating)) return;
        _navigating = true;
        try { await Navigation.PushAsync(new HistoryPage(_api, _selected)); }
        finally { _navigating = false; }
    }

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
