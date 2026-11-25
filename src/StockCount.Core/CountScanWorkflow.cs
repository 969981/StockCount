namespace StockCount.Core;

/// <summary>Data returned from the server after resolving a full label QR.</summary>
public sealed record BoxScanData(
    string BoxNumber, string ItemCode, string? ItemName,
    string? Specification, string LotNumber, string StockUnit,
    bool AlreadyRecordedInCount);

public sealed class ScanCommitRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record LocationScanResult(string? Location, string? ErrorCode = null, string? Message = null);

public sealed record CountScanFeedback(string Kind, string? Message = null, int AutoDismissMs = 0);

/// <summary>Serialized before POST, restored after Android process termination.</summary>
public sealed record PendingSubmissionDraft(
    string Environment, string CountNumber, string CountName,
    string Location, string OriginalQr, BoxScanData Box,
    decimal Quantity, Guid RequestId);

public interface IPendingSubmissionStore
{
    Task SaveAsync(PendingSubmissionDraft draft, CancellationToken cancellationToken);
    Task<PendingSubmissionDraft?> LoadAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public interface ICountScanGateway
{
    Task<LocationScanResult> ResolveLocationAsync(string countNumber, string rawCode, CancellationToken ct);
    Task<BoxScanData?> ResolveLabelAsync(string countNumber, string qr, CancellationToken ct);
    Task<bool> SubmitAsync(string countNumber, Guid requestId, string qr,
        string boxNumber, string location, decimal qty, CancellationToken ct);
    Task<bool?> GetSubmissionAsync(string countNumber, Guid requestId, CancellationToken ct);
}

/// <summary>
/// UI-independent counting flow. A scan never edits a previously entered count
/// without explicit discard approval. A Committed result is acknowledged ONLY
/// after the UI has shown its 1000ms success popup.
/// </summary>
public sealed class CountScanWorkflow(
    ICountScanGateway gateway,
    string countNumber,
    string countName,
    string environment = "Test",
    IPendingSubmissionStore? pendingStore = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _committedAwaitingUi;

    public string CountNumber { get; } = countNumber;
    public string CountName { get; } = countName;
    public string EnvironmentName { get; } = environment;
    public string? Location { get; private set; }
    public BoxScanData? Box { get; private set; }
    public string? OriginalQr { get; private set; }
    public decimal? ManualQuantity { get; private set; }
    public Guid? PendingRequestId { get; private set; }
    public bool IsSuccessAwaitingDismiss => _committedAwaitingUi;
    public bool HasUnsavedDraft => Box is not null || ManualQuantity.HasValue;
    public bool HasUnresolvedSubmission => PendingRequestId.HasValue && !_committedAwaitingUi;

    private static bool HasExplicitLocationPrefix(string raw) =>
        LocationScanPolicy.Normalize(raw).StartsWith("LC.", StringComparison.OrdinalIgnoreCase);

    // This is only a routing hint, not a successful label identification.
    public static bool LooksLikeBoxQr(string raw) =>
        !HasExplicitLocationPrefix(raw) &&
        !LocationScanPolicy.TryParse(raw, out _) &&
        raw.Length is > 0 and <= 200 && !raw.Any(char.IsControl);

    public async Task<bool> RestorePendingAsync(CancellationToken ct = default)
    {
        if (pendingStore is null) return false;
        await _gate.WaitAsync(ct);
        try
        {
            var stored = await pendingStore.LoadAsync(ct);
            if (stored is null ||
                stored.Environment != EnvironmentName ||
                stored.CountNumber != CountNumber ||
                stored.RequestId == Guid.Empty)
                return false;

            Location = stored.Location;
            OriginalQr = stored.OriginalQr;
            Box = stored.Box;
            ManualQuantity = stored.Quantity;
            PendingRequestId = stored.RequestId;
            _committedAwaitingUi = false;
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<CountScanFeedback> HandleScanAsync(string raw, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new CountScanFeedback("Busy", "正在处理上次扫描，请稍后重扫。");
        try
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new CountScanFeedback("Error", "未识别到条码。");
            if (_committedAwaitingUi)
                return new CountScanFeedback("Busy", "提交成功提示尚未结束，请稍后重扫。");
            if (PendingRequestId.HasValue)
                return new CountScanFeedback("Unknown", "提交结果待确认，请先查询原请求结果。");

            raw = raw.TrimEnd('\r', '\n');
            if (HasUnsavedDraft && ManualQuantity.HasValue)
                return new CountScanFeedback("DraftNeedsConfirmation",
                    "当前箱尚未提交，继续扫描会丢弃已输入数量。");

            var explicitLocation = HasExplicitLocationPrefix(raw);
            var possibleLocation = LocationScanPolicy.TryParse(raw, out _);
            if (explicitLocation && !possibleLocation)
                return new CountScanFeedback("Error", LocationScanPolicy.Describe("LOCATION_FORMAT_INVALID"));

            if (possibleLocation)
            {
                // For short, unprefixed values, verify the location before treating
                // the same raw value as a possible label. Explicit LC is never a box.
                var result = await gateway.ResolveLocationAsync(CountNumber, raw, ct);
                var location = result.Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    if (!string.Equals(Location, location, StringComparison.Ordinal))
                    {
                        Location = location;
                        ClearBox();
                    }
                    return new CountScanFeedback("LocationUpdated");
                }
                if (explicitLocation || result.ErrorCode != "LOCATION_NOT_FOUND")
                    return new CountScanFeedback("Error", result.Message ?? LocationScanPolicy.Describe(result.ErrorCode ?? "LOCATION_LOOKUP_UNAVAILABLE"));
            }

            // QRCOD_0 is the authority; do not require ID. or @@ in a box QR.
            // Keep the original data (apart from scanner CR/LF terminators).
            if (raw.Length > 200 || raw.Any(char.IsControl))
                return new CountScanFeedback("Error", "二维码格式无效：长度不能超过 200 个字符，且不能含控制字符。");

            if (string.IsNullOrWhiteSpace(Location))
                return new CountScanFeedback("Error", "请先扫描盘点库位！");

            ClearBox();
            var resolved = await gateway.ResolveLabelAsync(CountNumber, raw, ct);
            if (resolved is null)
                return new CountScanFeedback("Error", "未找到该箱标签！请检查二维码及箱号。");
            if (resolved.AlreadyRecordedInCount)
                return new CountScanFeedback("Duplicate", "该箱号已扫描！");

            Box = resolved;
            OriginalQr = raw;
            ManualQuantity = null; // Never use QR QT as the actual quantity.
            return new CountScanFeedback("BoxResolved");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Only call after a confirmation dialog; never discard an unresolved write.</summary>
    public bool DiscardUnsubmittedDraft()
    {
        if (PendingRequestId.HasValue || _committedAwaitingUi)
            return false;
        ClearBox();
        return true;
    }

    public void EnterManualQuantity(decimal? quantity)
    {
        if (PendingRequestId.HasValue && ManualQuantity != quantity)
            throw new InvalidOperationException("提交结果未确认，不能改变原请求的数量。");
        if (_committedAwaitingUi)
            throw new InvalidOperationException("请先完成提交成功提示。");
        ManualQuantity = quantity;
    }

    public async Task<CountScanFeedback> SubmitAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new CountScanFeedback("Busy", "正在扫码或提交，请稍后。");
        try
        {
            if (_committedAwaitingUi)
                return new CountScanFeedback("Busy", "当前提交已成功，请等待确认提示结束。");
            if (Box is null || OriginalQr is null || Location is null)
                return new CountScanFeedback("Error", "请先扫描盘点库位和箱码。");
            if (!ManualQuantity.HasValue)
                return new CountScanFeedback("Error", "箱内数量为必填项！");
            var validation = CountWritePolicy.Validate(
                CountNumber, OriginalQr, Box.BoxNumber, Location, ManualQuantity.Value);
            if (validation is not null)
                return new CountScanFeedback("Error", "实盘数据无效：" + validation);

            PendingRequestId ??= Guid.NewGuid();
            if (pendingStore is not null)
            {
                try
                {
                    // Persist the same UUID and draft BEFORE sending the HTTP request.
                    await pendingStore.SaveAsync(new PendingSubmissionDraft(
                        EnvironmentName, CountNumber, CountName, Location, OriginalQr,
                        Box, ManualQuantity.Value, PendingRequestId.Value), ct);
                }
                catch (Exception)
                {
                    return new CountScanFeedback("Error", "无法安全保存待提交记录，请检查设备存储后重试。");
                }
            }

            try
            {
                var committed = await gateway.SubmitAsync(
                    CountNumber, PendingRequestId.Value, OriginalQr, Box.BoxNumber,
                    Location, ManualQuantity.Value, ct);
                if (!committed)
                    return new CountScanFeedback("Unknown", "提交结果待确认，请查询原请求ID。");
            }
            catch (ScanCommitRejectedException rejected)
            {
                // A definitive rejection is different from an unknown network outcome.
                if (rejected.Code == "BOX_ALREADY_RECORDED")
                    ClearBox();
                PendingRequestId = null;
                if (pendingStore is not null)
                {
                    try { await pendingStore.ClearAsync(CancellationToken.None); }
                    catch { /* Do not report the rejected transaction as successful. */ }
                }
                return new CountScanFeedback("Error", rejected.Message);
            }
            catch (Exception)
            {
                // Never clear or generate a new UUID after a possibly committed request.
                return new CountScanFeedback("Unknown", "网络异常，提交结果待确认，请查询原请求ID。");
            }

            _committedAwaitingUi = true;
            return new CountScanFeedback("Submitted", "提交成功！", 1000);
        }
        finally { _gate.Release(); }
    }

    public async Task<CountScanFeedback> CheckPendingAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new CountScanFeedback("Busy", "正在处理，请稍后。");
        try
        {
            if (_committedAwaitingUi)
                return new CountScanFeedback("Submitted", "提交成功！", 1000);
            if (!PendingRequestId.HasValue)
                return new CountScanFeedback("Ready");
            try
            {
                var confirmed = await gateway.GetSubmissionAsync(
                    CountNumber, PendingRequestId.Value, ct);
                if (confirmed is true)
                {
                    _committedAwaitingUi = true;
                    return new CountScanFeedback("Submitted", "提交成功！", 1000);
                }
                if (confirmed is false)
                    return new CountScanFeedback("Unknown",
                        "未找到已提交记录。请保留原 UUID，使用“重试原提交”继续。");
                return new CountScanFeedback("Unknown", "服务器暂时无法确认提交结果。");
            }
            catch
            {
                return new CountScanFeedback("Unknown", "查询提交结果失败，请保持当前输入。");
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Call only after rendering the full success popup duration.</summary>
    public async Task AcknowledgeSuccessAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_committedAwaitingUi) return;
            if (pendingStore is not null)
                await pendingStore.ClearAsync(ct);
            ClearBox();
            PendingRequestId = null;
            _committedAwaitingUi = false;
        }
        finally { _gate.Release(); }
    }

    private void ClearBox()
    {
        Box = null;
        OriginalQr = null;
        ManualQuantity = null;
    }
}
