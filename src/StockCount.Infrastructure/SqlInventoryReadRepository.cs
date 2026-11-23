using Dapper;
using Microsoft.Data.SqlClient;
using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Infrastructure;

/// <summary>
/// Read-only Sage X3 queries. All SQL identifiers come from the two-value server whitelist.
/// Absolutely no ERP INSERT/UPDATE/DELETE is implemented here.
/// </summary>
public sealed class SqlInventoryReadRepository(string connectionString,
    bool enableTestWrites = false, bool enableProductionWrites = false,
    bool testSchemaReviewed = false, bool productionSchemaReviewed = false) : IInventoryReadRepository
{
    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:SageX3 is not configured.");

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<InventoryCountDto>> GetCountsAsync(
        SelectedEnvironment environment, int skip, int take, string? search,
        CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT H.CUNNUM_0 AS CountNumber, H.CUNDES_0 AS Description,
                   H.STOFCY_0 AS SiteCode, H.WRH_0 AS WarehouseCode,
                   H.CUNDAT_0 AS CountDate, CAST(H.CUNSTA_0 AS int) AS StatusCode,
                   CAST(H.CUNTYP_0 AS nvarchar(30)) AS CountType
            FROM {schema}.[XCOUTH] H
            WHERE (@Search IS NULL
                   OR CHARINDEX(@Search,H.CUNNUM_0)>0
                   OR CHARINDEX(@Search,H.CUNDES_0)>0)
            ORDER BY H.CUNDAT_0 DESC,H.CUNNUM_0 DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var records = await connection.QueryAsync<CountRow>(
            new CommandDefinition(sql, new { Skip=skip, Take=take, Search=search }, cancellationToken:cancellationToken));
        return records.Select(row => ToDto(row, environment)).ToArray();
    }

    public async Task<InventoryCountDto?> GetCountAsync(
        SelectedEnvironment environment, string countNumber,
        CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT H.CUNNUM_0 AS CountNumber,H.CUNDES_0 AS Description,
                   H.STOFCY_0 AS SiteCode,H.WRH_0 AS WarehouseCode,
                   H.CUNDAT_0 AS CountDate,CAST(H.CUNSTA_0 AS int) AS StatusCode,
                   CAST(H.CUNTYP_0 AS nvarchar(30)) AS CountType
            FROM {schema}.[XCOUTH] H
            WHERE H.CUNNUM_0=@CountNumber;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var record = await connection.QuerySingleOrDefaultAsync<CountRow>(
            new CommandDefinition(sql, new { CountNumber=countNumber }, cancellationToken:cancellationToken));
        return record is null ? null : ToDto(record, environment);
    }

    public async Task<LocationRecord?> FindLocationAsync(
        SelectedEnvironment environment, string siteCode, string warehouseCode,
        string locationCode, CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT S.LOC_0 AS LocationCode,S.STOFCY_0 AS SiteCode,
                   S.WRH_0 AS WarehouseCode
            FROM {schema}.[STOLOC] S
            WHERE S.STOFCY_0=@Site AND S.WRH_0=@Warehouse AND S.LOC_0=@Location;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LocationRecord>(
            new CommandDefinition(sql, new { Site=siteCode, Warehouse=warehouseCode, Location=locationCode },
                cancellationToken:cancellationToken));
    }

    public async Task<LabelRecord?> FindLabelAsync(
        SelectedEnvironment environment, string countNumber, string fullQr,
        CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
                   I.YITMDES4_0 AS ItemName,I.ITMDES2_0 AS Specification,
                   L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit,
                   L.QTY_0 AS LabelQty,CAST(L.STA_0 AS int) AS LabelStatusCode,
                   CAST(CASE WHEN EXISTS (
                     SELECT 1 FROM {schema}.[XCOUTD] D
                     WHERE D.CUNNUM_0=@CountNumber AND D.BOXNUM_0=L.PID_0
                   ) THEN 1 ELSE 0 END AS bit) AS AlreadyRecordedInCount
            FROM {schema}.[XLABELDTL] L
            LEFT JOIN {schema}.[ITMMASTER] I ON I.ITMREF_0=L.ITMREF_0
            WHERE L.QRCOD_0=@FullQr;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        // Fail on ambiguous matching QR rather than silently selecting an arbitrary label.
        return await connection.QuerySingleOrDefaultAsync<LabelRecord>(
            new CommandDefinition(sql,new { CountNumber=countNumber, FullQr=fullQr },
                cancellationToken:cancellationToken));
    }

    public async Task<IReadOnlyList<CountItemDto>> GetItemsAsync(
        SelectedEnvironment environment, string countNumber, int skip, int take,
        CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT CONVERT(nvarchar(40),D.ROWID) AS DetailId,
                   D.CUNNUM_0 AS CountNumber,D.BOXNUM_0 AS BoxNumber,
                   D.LOC_0 AS LocationCode,D.ITMREF_0 AS ItemCode,
                   D.LOT_0 AS LotNumber,D.PCU_0 AS PackageUnit,
                   D.QTYPCU_0 AS QuantityPcu,D.QTYSTU_0 AS QuantityStu,
                   D.CREDATTIM_0 AS CreatedAt
            FROM {schema}.[XCOUTD] D
            WHERE D.CUNNUM_0=@CountNumber
            ORDER BY D.ROWID DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var result = await connection.QueryAsync<CountItemRow>(
            new CommandDefinition(sql,new { CountNumber=countNumber,Skip=skip,Take=take },
                cancellationToken:cancellationToken));
        return result.Select(x=>new CountItemDto(
            x.DetailId,x.CountNumber,x.BoxNumber,x.LocationCode,x.ItemCode,x.LotNumber,
            x.PackageUnit,x.QuantityPcu,x.QuantityStu,x.CreatedAt)).ToArray();
    }

    private InventoryCountDto ToDto(CountRow row, SelectedEnvironment environment)
    {
        var isTest = environment.Name == "Test";
        var reviewed = isTest ? testSchemaReviewed : productionSchemaReviewed;
        var switchedOn = isTest ? enableTestWrites : enableProductionWrites;
        var enabled = reviewed && switchedOn && row.StatusCode == 0;
        var reason = enabled ? null
            : row.StatusCode != 0 ? "COUNT_NOT_WRITABLE"
            : !switchedOn ? (isTest ? "ERP_WRITE_UNAVAILABLE" : "PRODUCTION_WRITE_DISABLED")
            : !reviewed ? "SCHEMA_NOT_APPROVED" : "ERP_WRITE_UNAVAILABLE";
        return new InventoryCountDto(row.CountNumber,row.Description,row.SiteCode,row.WarehouseCode,
            row.CountDate,row.StatusCode,row.CountType,enabled,reason);
    }

    private sealed class CountRow
    {
        public string CountNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SiteCode { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public DateTime CountDate { get; set; }
        public int StatusCode { get; set; }
        public string CountType { get; set; } = string.Empty;
    }

    private sealed class CountItemRow
    {
        public string DetailId { get; set; } = string.Empty;
        public string CountNumber { get; set; } = string.Empty;
        public string BoxNumber { get; set; } = string.Empty;
        public string LocationCode { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string LotNumber { get; set; } = string.Empty;
        public string PackageUnit { get; set; } = string.Empty;
        public decimal QuantityPcu { get; set; }
        public decimal QuantityStu { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
