using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StockCount.Core;

public sealed record HistoryQuery(string Environment, string CountNumber, string? BoxSearch, string? Location)
{
    public static bool TryCreate(string environment, string countNumber, string? boxSearch,
        string? location, out HistoryQuery query)
    {
        boxSearch = string.IsNullOrWhiteSpace(boxSearch) ? null : boxSearch.Trim();
        location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        query = new(environment, countNumber, boxSearch, location);
        return !string.IsNullOrWhiteSpace(countNumber) && countNumber.Length <= 30 &&
            (boxSearch is null || (boxSearch.Length <= 20 && !boxSearch.Any(char.IsControl))) &&
            (location is null || (location.Length <= 10 && !location.Any(char.IsControl)));
    }

    public string Scope => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { Environment, CountNumber, BoxSearch, Location }))));
}

public sealed record HistoryCursor(string UpperRowId, string BeforeRowId)
{
    public static bool IsRowId(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 39) return false;
        var digits = text[0] == '-' ? text[1..] : text;
        return digits.Length is >= 1 and <= 38 && digits.All(c => c is >= '0' and <= '9') &&
            BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) &&
            number.ToString(CultureInfo.InvariantCulture) == text;
    }

    public string Encode(HistoryQuery query) => $"h1:{UpperRowId}:{BeforeRowId}:{query.Scope}";

    public static bool TryDecode(string? value, HistoryQuery query, out HistoryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value)) return true;
        if (value.Length > 150) return false;
        var parts = value.Split(':');
        if (parts.Length != 4 || parts[0] != "h1" || parts[3] != query.Scope ||
            !IsRowId(parts[1]) || !IsRowId(parts[2]) ||
            BigInteger.Parse(parts[2], CultureInfo.InvariantCulture) >
            BigInteger.Parse(parts[1], CultureInfo.InvariantCulture)) return false;
        cursor = new(parts[1], parts[2]);
        return true;
    }
}

public static class HistoryPagingPolicy
{
    public const int PageSize = 50;
    public const int MaxBufferedRows = 500;
    public static bool ValidSize(int size) => size is >= 1 and <= 100;
    public static bool CanAutoLoad(int buffered, bool hasMore, bool busy, bool retryRequired) =>
        buffered < MaxBufferedRows && hasMore && !busy && !retryRequired;
}
