using StockCount.Core;

namespace StockCount.Infrastructure;

// Shared by scanning and transactional submission. Schema is obtained only
// from the validated environment selector. All other inputs are parameters.
internal static class LocationLookupQuery
{
    internal static string Sql(string schema) => $"""
        SELECT COUNT_BIG(*) AS TotalMatches,
               COUNT_BIG(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site)
                                   AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse)
                              THEN 1 END) AS ScopedMatches,
               MIN(CASE WHEN (@CheckSite=0 OR S.STOFCY_0=@Site)
                         AND (@CheckWarehouse=0 OR S.WRH_0=@Warehouse)
                        THEN S.LOC_0 END) AS LocationCode
        FROM {schema}.[STOLOC] S
        WHERE S.LOC_0=@Location;
        """;

    internal static object Parameters(LocationScope scope, string site, string warehouse, string location) =>
        new { CheckSite=scope.CheckSite, CheckWarehouse=scope.CheckWarehouse,
              Site=site, Warehouse=warehouse, Location=location };
}
