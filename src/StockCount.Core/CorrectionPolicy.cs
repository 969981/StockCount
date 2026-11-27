namespace StockCount.Core;

/// <summary>Server-resolved snapshot; persisted as part of the pending draft for safe retry.</summary>
public sealed record BoxCorrection(string Mode, string? DetailId, string? CountNumber,
    string? BoxNumber, string? LocationCode, decimal? OldQuantity, string? StockUnit,
    DateTime? UpdatedAt, string? Version, string? Message);

public static class CorrectionPolicy
{
    public static string Classify(int matches, bool sameCount, bool sameLocation,
        bool compatible, bool enabled) =>
        matches == 0 ? "New" : matches != 1 ? "Ambiguous" :
        !sameCount ? "CountConflict" : !sameLocation ? "LocationConflict" :
        !compatible ? "Unsupported" : !enabled ? "UpdateDisabled" : "Update";

    public static bool ValidVersion(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    public static bool Matches(BoxCorrection snapshot, string count, string location, string box) =>
        snapshot.Mode == "Update" && HistoryCursor.IsRowId(snapshot.DetailId) &&
        string.Equals(snapshot.CountNumber, count, StringComparison.Ordinal) &&
        string.Equals(snapshot.LocationCode, location, StringComparison.Ordinal) &&
        string.Equals(snapshot.BoxNumber, box, StringComparison.Ordinal) &&
        snapshot.OldQuantity.HasValue && ValidVersion(snapshot.Version);
}
