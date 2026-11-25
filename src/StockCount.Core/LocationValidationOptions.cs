namespace StockCount.Core;

public sealed record LocationScope(bool CheckSite = false, bool CheckWarehouse = false)
{
    // XCOUTD stores the location code, not a STOLOC row ID. With either
    // scope disabled, repeated codes may legitimately exist in that scope.
    public string? Classify(long total, long scoped) =>
        total == 0 ? "LOCATION_NOT_FOUND" :
        scoped == 0 ? "LOCATION_OUTSIDE_WAREHOUSE" :
        CheckSite && CheckWarehouse && scoped > 1 ? "LOCATION_AMBIGUOUS" : null;

    public string Describe(string code) => code != "LOCATION_OUTSIDE_WAREHOUSE"
        ? LocationScanPolicy.Describe(code)
        : (CheckSite, CheckWarehouse) switch
        {
            (true, true) => "该库位存在，但不属于当前盘点单的站点或仓库！",
            (true, false) => "该库位存在，但不属于当前盘点单的库存站点！",
            (false, true) => "该库位存在，但不属于当前盘点单的仓库！",
            _ => LocationScanPolicy.Describe(code)
        };
}

public sealed record LocationValidationOptions(
    bool TestCheckSite = false, bool TestCheckWarehouse = false,
    bool ProductionCheckSite = false, bool ProductionCheckWarehouse = false)
{
    public LocationScope For(string environment) => environment switch
    {
        "Test" => new(TestCheckSite, TestCheckWarehouse),
        "Production" => new(ProductionCheckSite, ProductionCheckWarehouse),
        _ => throw new ArgumentOutOfRangeException(nameof(environment))
    };
}
