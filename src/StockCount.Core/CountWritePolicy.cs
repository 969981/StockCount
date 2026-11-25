using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StockCount.Core;

/// <summary>Pure rules, independent of HTTP and Sage X3. No barcode QTY is used as a count.</summary>
public static class CountWritePolicy
{
    public static string? Validate(string countNumber, string qr, string box, string location, decimal quantity)
    {
        if (string.IsNullOrWhiteSpace(countNumber) || countNumber.Length > 30)
            return "COUNT_NUMBER_INVALID";
        if (string.IsNullOrWhiteSpace(qr) || qr.Length > 200)
            return "BARCODE_UNSUPPORTED";
        if (string.IsNullOrWhiteSpace(box) || box.Length > 20)
            return "BOX_NUMBER_TOO_LONG";
        if (string.IsNullOrWhiteSpace(location) || location.Length > 10)
            return "LOCATION_INVALID";
        // Quantity boundaries are intentionally conservative for numeric(28,13):
        // negative values are invalid; zero is allowed (empty box is a legitimate count).
        if (quantity < 0 || quantity >= 1000000000000000m ||
            decimal.Round(quantity, 13) != quantity)
            return "COUNT_QUANTITY_INVALID";
        if (new[] { countNumber, qr, box, location }.Any(x => x.Any(char.IsControl)))
            return "BARCODE_UNSUPPORTED";
        return null;
    }

    public static byte[] PayloadHash(
        string environment, string countNumber, string qr, string box, string location, decimal quantity)
    {
        var payload = string.Join("\u001F", environment, countNumber, qr, box, location,
            quantity.ToString("0.############################", CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    // Counting is performed in the label's stock unit, not its package unit.
    public const decimal CountUnitFactor = 1m;

    public sealed record CountUnits(string PackageUnit, decimal UnitFactor, decimal QuantityStu, decimal QuantityPcu);

    // No original packaging data is accepted by this mapping function.
    public static CountUnits MapCountUnits(string stockUnit, decimal manualQuantity)
    {
        if (!IsValidStockUnit(stockUnit))
            throw new ArgumentException("Invalid stock unit.", nameof(stockUnit));
        return new CountUnits(stockUnit, CountUnitFactor, manualQuantity, manualQuantity);
    }

    public static bool IsValidStockUnit(string? stockUnit) =>
        !string.IsNullOrWhiteSpace(stockUnit) &&
        stockUnit.Length <= 3 &&
        !stockUnit.Any(char.IsControl);
}
