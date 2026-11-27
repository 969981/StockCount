namespace StockCount.Contracts;

public sealed record InventoryCountDto(
    string CountNumber,
    string Description,
    string SiteCode,
    string WarehouseCode,
    DateTime CountDate,
    int StatusCode,
    string CountType,
    bool CanSubmit,
    string? CannotSubmitReason
);

public sealed record CountPage(string Environment, IReadOnlyList<InventoryCountDto> Items, string? NextCursor);
public sealed record CountDetails(string Environment, InventoryCountDto Count);

public sealed record ResolveLocationRequest(string CountNumber, string RawCode);
public sealed record ResolvedLocation(
    string Environment,
    string LocationCode,
    string SiteCode,
    string WarehouseCode,
    bool IsValid,
    string? Message
);

public sealed record ResolveLabelRequest(string CountNumber, string RawQrCode)
{
    // Absent for legacy clients: preserve duplicate-rejection contract.
    public string? LocationCode { get; init; }
}

public sealed record RecordedCountItem(string DetailId, string CountNumber, string BoxNumber,
    string LocationCode, decimal QuantityStu, decimal QuantityPcu, string StockUnit,
    DateTime UpdatedAt, string Version);

public sealed record CountBoxDecision(string Mode, RecordedCountItem? Existing, string? Message);

public sealed record UpdateCountItemRequest(string RawQrCode, string BoxNumber,
    string CountedLocation, decimal? CountedQtyStu, string ExpectedVersion);
public sealed record ResolvedLabel(
    string Environment,
    string BoxNumber,
    string ItemCode,
    string? ItemName,
    string? Specification,
    string LotNumber,
    string StockUnit,
    decimal LabelQty,
    int LabelStatusCode,
    bool AlreadyRecordedInCount
)
{
    public CountBoxDecision? Decision { get; init; }
}

public sealed record CountItemDto(
    string DetailId,
    string CountNumber,
    string BoxNumber,
    string LocationCode,
    string ItemCode,
    string LotNumber,
    string PackageUnit,
    decimal QuantityPcu,
    decimal QuantityStu,
    DateTime CreatedAt
)
{
    public DateTime? UpdatedAt { get; init; }
    public decimal? LastCorrectionOldQuantity { get; init; }
    public decimal? LastCorrectionNewQuantity { get; init; }
    public DateTime? LastCorrectionAtUtc { get; init; }
}
public sealed record CountItemPage(string Environment, IReadOnlyList<CountItemDto> Items, string? NextCursor);

/// <summary>Counts are returned on the first page only; continuation uses the same upper ROWID.</summary>
public sealed record CountHistoryPage(string Environment, IReadOnlyList<CountItemDto> Items,
    string? NextCursor, long? TotalCount, long? FilteredCount);

public sealed record SaveCountItemRequest(
    string RawQrCode,
    string BoxNumber,
    string CountedLocation,
    decimal? CountedQtyStu
);


/// <summary>One committed XCOUTD row. Both values equal the manually entered STU count.</summary>
public sealed record SaveCountItemResult(
    string Environment,
    string CountNumber,
    string BoxNumber,
    string DetailId,
    decimal SavedQtyStu,
    decimal SavedQtyPcu,
    string StockUnit,
    Guid RequestId,
    string Status,
    bool Replayed
)
{
    public string Operation { get; init; } = "Insert";
    public string? LocationCode { get; init; }
}
