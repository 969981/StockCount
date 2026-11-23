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
        var existing = await LoadAsync(cancellationToken);
        if (existing is not null &&
            (existing.RequestId != draft.RequestId ||
             existing.Environment != draft.Environment ||
             existing.CountNumber != draft.CountNumber))
        {
            throw new InvalidOperationException(
                "设备中有另一笔尚未确认的提交，必须先恢复原单据并确认结果。");
        }

        await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(draft, Json));
    }

    public async Task<PendingSubmissionDraft?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await SecureStorage.Default.GetAsync(Key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return JsonSerializer.Deserialize<PendingSubmissionDraft>(value, Json)
            ?? throw new InvalidDataException("待确认盘点草稿已损坏，不能自动覆盖。");
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
