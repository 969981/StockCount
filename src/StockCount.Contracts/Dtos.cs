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

public sealed record ResolveLabelRequest(string CountNumber, string RawQrCode);
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
);

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
);
public sealed record CountItemPage(string Environment, IReadOnlyList<CountItemDto> Items, string? NextCursor);

public sealed record SaveCountItemRequest(
    string RawQrCode,
    string BoxNumber,
    string CountedLocation,
    decimal CountedQtyStu
);
