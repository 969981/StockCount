using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Infrastructure;

public interface IInventoryReadRepository
{
    Task PingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryCountDto>> GetCountsAsync(
        SelectedEnvironment environment, int skip, int take, string? search,
        CancellationToken cancellationToken);
    Task<InventoryCountDto?> GetCountAsync(
        SelectedEnvironment environment, string countNumber, CancellationToken cancellationToken);
    Task<LocationLookup> InspectLocationAsync(
        SelectedEnvironment environment, string siteCode, string warehouseCode,
        string locationCode, CancellationToken cancellationToken);
    Task<LabelRecord?> FindLabelAsync(
        SelectedEnvironment environment, string countNumber, string fullQr,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CountItemDto>> GetItemsAsync(
        SelectedEnvironment environment, string countNumber, int skip, int take,
        CancellationToken cancellationToken);
}

public sealed class LocationLookup
{
    public long TotalMatches { get; set; }
    public long ScopedMatches { get; set; }
    public string? LocationCode { get; set; }
}
public sealed class LabelRecord
{
    public string BoxNumber { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string? ItemName { get; set; }
    public string? Specification { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string StockUnit { get; set; } = string.Empty;
    public decimal LabelQty { get; set; }
    public int LabelStatusCode { get; set; }
    public bool AlreadyRecordedInCount { get; set; }
}
