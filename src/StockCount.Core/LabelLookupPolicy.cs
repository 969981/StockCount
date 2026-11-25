namespace StockCount.Core;

/// <summary>Exact QR first; only a definite miss permits a structured ID fallback.</summary>
public static class LabelLookupPolicy
{
    public static string? ExtractBoxId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.Contains("@@", StringComparison.Ordinal))
            return null;
        var ids = raw.Split("@@", StringSplitOptions.None)
            .Where(field => field.StartsWith("ID.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (ids.Length != 1) return null;
        var id = ids[0][3..];
        // The field must be complete, never truncated or guessed.
        if (id.Length is < 1 or > 20 ||
            !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '|' or '-' or '_' or '.'))
            return null;
        return id;
    }

    public static async Task<T[]> FindAsync<T>(string raw,
        Func<string, Task<T[]>> exactQr,
        Func<string, Task<T[]>> byBoxId)
    {
        var matches = await exactQr(raw);
        // Ambiguous exact matches are errors, not a reason to fall back.
        if (matches.Length != 0) return matches;
        var boxId = ExtractBoxId(raw);
        return boxId is null ? matches : await byBoxId(boxId);
    }
}
