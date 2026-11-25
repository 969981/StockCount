using System.Text.Json;
using StockCount.Core;

namespace StockCount.Pad.Services;

/// <summary>
/// Survives Android process death. MAUI SecureStorage uses platform protection;
/// never save the full QR, count quantity or idempotency token in Preferences.
/// </summary>
public sealed class SecurePendingSubmissionStore : IPendingSubmissionStore
{
    private const string Key = "StockCount.PendingSubmission.V1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(PendingSubmissionDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null &&
            (existing.RequestId != draft.RequestId ||
             existing.Environment != draft.Environment ||
             existing.CountNumber != draft.CountNumber))
        {
            throw new InvalidOperationException(
                "设备中有另一笔尚未确认的提交，必须先恢复原单据并确认结果。");
        }

        using var timing = ClientPerformance.Shared.Measure(ClientPerfStage.DraftWrite);
        await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(draft, Json)).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PendingSubmissionDraft?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timing = ClientPerformance.Shared.Measure(ClientPerfStage.DraftRead);
        return await Task.Run(async () =>
        {
            var value = await SecureStorage.Default.GetAsync(Key).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(value)) return null;
            return JsonSerializer.Deserialize<PendingSubmissionDraft>(value, Json)
                ?? throw new InvalidDataException("待确认盘点草稿已损坏，不能自动覆盖。");
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        using var timing = ClientPerformance.Shared.Measure(ClientPerfStage.DraftClear);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecureStorage.Default.Remove(Key);
        }, cancellationToken).ConfigureAwait(false);
    }
}
