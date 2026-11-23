namespace StockCount.Core;

public sealed record BoxScanData(
    string BoxNumber, string ItemCode, string? ItemName,
    string? Specification, string LotNumber, string StockUnit,
    bool AlreadyRecordedInCount);

public sealed record CountScanFeedback(string Kind, string? Message = null, int AutoDismissMs = 0);

public interface ICountScanGateway
{
    Task<string?> ResolveLocationAsync(string countNumber, string rawCode, CancellationToken ct);
    Task<BoxScanData?> ResolveLabelAsync(string countNumber, string qr, CancellationToken ct);
    Task<bool> SubmitAsync(string countNumber, Guid requestId, string qr,
        string boxNumber, string location, decimal qty, CancellationToken ct);
    Task<bool?> GetSubmissionAsync(string countNumber, Guid requestId, CancellationToken ct);
}

/// <summary>
/// One active count order. The client cannot type a location: only HandleScanAsync
/// may set it after successful backend validation. The input quantity starts blank.
/// </summary>
public sealed class CountScanWorkflow(ICountScanGateway gateway, string countNumber, string countName)
{
    private readonly SemaphoreSlim _scanGate = new(1,1);
    public string CountNumber { get; } = countNumber;
    public string CountName { get; } = countName;
    public string? Location { get; private set; }
    public BoxScanData? Box { get; private set; }
    public string? OriginalQr { get; private set; }
    public decimal? ManualQuantity { get; private set; }
    public Guid? PendingRequestId { get; private set; }

    public static bool LooksLikeBoxQr(string raw) =>
        raw.StartsWith("ID.", StringComparison.OrdinalIgnoreCase) && raw.Contains("@@",StringComparison.Ordinal);

    public async Task<CountScanFeedback> HandleScanAsync(string raw, CancellationToken ct=default)
    {
        if (!await _scanGate.WaitAsync(0,ct))
            return new CountScanFeedback("Busy", "正在处理上次扫码，请稍后。");
        try
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new CountScanFeedback("Error","未识别到条码。");
            if (PendingRequestId.HasValue)
                return new CountScanFeedback("Error","上次提交结果尚未确认，请先查询结果，避免重复盘点。");

            raw = raw.TrimEnd('\r','\n');
            if (!LooksLikeBoxQr(raw))
            {
                var location = await gateway.ResolveLocationAsync(CountNumber,raw,ct);
                if (!string.IsNullOrWhiteSpace(location))
                {
                    if (!string.Equals(Location,location,StringComparison.Ordinal))
                    {
                        Location=location;
                        ClearBox();
                    }
                    return new CountScanFeedback("LocationUpdated");
                }
                if (string.IsNullOrEmpty(Location))
                    return new CountScanFeedback("Error","请先扫描盘点库位！");
                return new CountScanFeedback("Error","无法识别有效库位或箱标签。");
            }

            if (string.IsNullOrWhiteSpace(Location))
                return new CountScanFeedback("Error","请先扫描盘点库位！");

            // Clearing the prior box on attempted new barcode avoids stale values.
            ClearBox();
            var resolved = await gateway.ResolveLabelAsync(CountNumber,raw,ct);
            if (resolved is null)
                return new CountScanFeedback("Error","未找到该箱标签！");
            if (resolved.AlreadyRecordedInCount)
                return new CountScanFeedback("Error","该箱号已扫描！");
            Box=resolved;
            OriginalQr=raw;
            ManualQuantity=null;
            return new CountScanFeedback("BoxResolved");
        }
        finally { _scanGate.Release(); }
    }

    public void EnterManualQuantity(decimal? quantity) => ManualQuantity=quantity;

    public async Task<CountScanFeedback> SubmitAsync(CancellationToken ct=default)
    {
        if (!await _scanGate.WaitAsync(0,ct))
            return new CountScanFeedback("Busy","正在处理扫码或提交请求。");
        try
        {
            if (Box is null || OriginalQr is null || Location is null)
                return new CountScanFeedback("Error","请先扫描盘点库位和箱码。");
            if (!ManualQuantity.HasValue)
                return new CountScanFeedback("Error","箱内数量为必填项！");
            if (CountWritePolicy.Validate(CountNumber,OriginalQr,Box.BoxNumber,Location,ManualQuantity.Value) is { } validation)
                return new CountScanFeedback("Error",$"输入无法提交：{validation}");

            // Request ID survives network errors and must be reused for the same draft.
            PendingRequestId ??= Guid.NewGuid();
            try
            {
                var committed = await gateway.SubmitAsync(
                    CountNumber,PendingRequestId.Value,OriginalQr,Box.BoxNumber,
                    Location,ManualQuantity.Value,ct);
                if (!committed) return new CountScanFeedback("Error","提交结果不确定，请查询原请求ID。");
            }
            catch (Exception)
            {
                // Keep ALL draft fields AND request ID after unknown outcome.
                return new CountScanFeedback("Unknown","网络异常，提交结果未知，请先查询原请求ID。");
            }
            ClearBox();
            PendingRequestId=null;
            return new CountScanFeedback("Submitted","提交成功！",1000);
        }
        finally { _scanGate.Release(); }
    }

    public async Task<CountScanFeedback> CheckPendingAsync(CancellationToken ct=default)
    {
        if (!PendingRequestId.HasValue)
            return new CountScanFeedback("Ready");
        try
        {
            var confirmed = await gateway.GetSubmissionAsync(CountNumber,PendingRequestId.Value,ct);
            if (confirmed is true)
            {
                ClearBox();
                PendingRequestId=null;
                return new CountScanFeedback("Submitted","提交成功！",1000);
            }
            if (confirmed is false)
                return new CountScanFeedback("Ready","服务器未找到成功记录，重新提交时继续使用原请求ID。");
            return new CountScanFeedback("Unknown","服务器暂时无法确认提交结果。");
        }
        catch (Exception)
        {
            return new CountScanFeedback("Unknown","查询提交结果失败，请保持当前输入信息。");
        }
    }

    private void ClearBox()
    {
        Box=null;
        OriginalQr=null;
        ManualQuantity=null;
        // PendingRequestId intentionally not reset here.
    }
}
