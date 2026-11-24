namespace StockCount.Core;

/// <summary>Confirmed LC. scanner prefix; plain location values remain compatible.</summary>
public static class LocationScanPolicy
{
    public static string Normalize(string? raw) => (raw ?? "").TrimEnd('\r', '\n').Trim(' ');

    public static bool TryParse(string? raw, out string locationCode)
    {
        locationCode = "";
        var text = Normalize(raw);
        if (text.StartsWith("LC.", StringComparison.OrdinalIgnoreCase))
            text = text[3..];

        // A standalone location label contains one LC value, not a box or
        // another composite field. Never truncate or strip a second prefix.
        if (text.Contains("@@", StringComparison.Ordinal) ||
            new[] { "LC.", "ID.", "PT.", "LN.", "MT.", "LT.", "QT.", "PG." }
                .Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            !IsValidFormat(text) || text != text.Trim())
            return false;

        locationCode = text;
        return true;
    }

    public static bool IsValidFormat(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= 10 && !code.Any(char.IsControl);

    public static string? ClassifyLookup(long totalMatches, long scopedMatches) =>
        scopedMatches > 1 ? "LOCATION_AMBIGUOUS" :
        scopedMatches == 1 ? null :
        totalMatches == 0 ? "LOCATION_NOT_FOUND" : "LOCATION_OUTSIDE_WAREHOUSE";

    public static string Describe(string code) => code switch
    {
        "LOCATION_FORMAT_INVALID" => "库位编码格式不符合要求：请扫描 LC. 开头的库位标签，提取后的库位须为 1–10 个字符，不能含控制字符。",
        "LOCATION_NOT_FOUND" => "当前帐套不存在该库位，请核对库位编码！",
        "LOCATION_OUTSIDE_WAREHOUSE" => "该库位存在，但不属于当前盘点单的站点或仓库！",
        "LOCATION_AMBIGUOUS" => "当前仓库存在重复库位记录，请联系后台管理员核查！",
        "COUNT_NOT_FOUND" => "当前盘点单不存在，请返回列表重新选择。",
        "LOCATION_INVALID" => "库位校验未通过：旧版后台未区分不存在、格式或仓库错误，请更新后台后重试。",
        _ => "库位查询失败，请检查后台服务或数据库连接！"
    };
}
