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
    bool testSchemaReviewed = false, bool productionSchemaReviewed = false,
    LocationValidationOptions? locationValidation = null) : IInventoryReadRepository, IInventoryHistoryRepository
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

    public async Task<LocationLookup> InspectLocationAsync(
        SelectedEnvironment environment, string siteCode, string warehouseCode,
        string locationCode, CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var scope = (locationValidation ?? new LocationValidationOptions()).For(environment.Name);
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<LocationLookup>(
            new CommandDefinition(LocationLookupQuery.Sql(schema),
                LocationLookupQuery.Parameters(scope, siteCode, warehouseCode, locationCode),
                cancellationToken:cancellationToken));
    }

    public async Task<LabelRecord?> FindLabelAsync(
        SelectedEnvironment environment, string countNumber, string fullQr,
        CancellationToken cancellationToken)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var sql = $"""
            SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
                   I.YITMDES4_0 AS ItemName,I.ITMDES2_0 AS Specification,
                   L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit,
                   L.QTY_0 AS LabelQty,CAST(L.STA_0 AS int) AS LabelStatusCode,
                   {BoxDuplicateQuery.LabelProjection(environment)}
            FROM {schema}.[XLABELDTL] L
            LEFT JOIN {schema}.[ITMMASTER] I ON I.ITMREF_0=L.ITMREF_0
            """;
        await using var connection = await OpenAsync(cancellationToken);
        async Task<LabelRecord[]> QueryAsync(string predicate, string value) =>
            (await connection.QueryAsync<LabelRecord>(
                new CommandDefinition(sql + predicate,
                    new { Value=value },
                    cancellationToken:cancellationToken))).ToArray();

        var labels = await LabelLookupPolicy.FindAsync(fullQr,
            value => QueryAsync(" WHERE L.QRCOD_0=@Value;", value),
            value => QueryAsync(" WHERE L.PID_0=@Value;", value));
        if (labels.Length > 1)
            throw new CountWriteException(409, "LABEL_AMBIGUOUS",
                "二维码或箱号对应多条标签记录，请联系管理员核查。");
        return labels.SingleOrDefault();
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

    public async Task<CountHistoryPage?> GetHistoryAsync(
        SelectedEnvironment environment, HistoryQuery query, HistoryCursor? cursor,
        int size, CancellationToken cancellationToken)
    {
        if (await GetCountAsync(environment, query.CountNumber, cancellationToken) is null) return null;
        await using var connection = await OpenAsync(cancellationToken);
        HistorySummary? summary = null;
        if (cursor is null)
            summary = await connection.QuerySingleAsync<HistorySummary>(new CommandDefinition(
                HistoryReadQuery.Summary(environment), new { query.CountNumber, query.BoxSearch, query.Location },
                cancellationToken: cancellationToken));
        var upper = cursor?.UpperRowId ?? summary?.UpperRowId;
        if (upper is null)
            return new CountHistoryPage(environment.Name, Array.Empty<CountItemDto>(), null,
                summary?.TotalCount, summary?.FilteredCount);
        var withReceipts = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(@Name,N'U') IS NULL THEN 0 ELSE 1 END;",
            new { Name=EnvironmentSelector.TablePrefix(environment)+".[StockCountOperationReceipt]" },
            cancellationToken:cancellationToken)) == 1;
        var rows = (await connection.QueryAsync<CountItemRow>(new CommandDefinition(
            HistoryReadQuery.Page(environment, cursor is not null, withReceipts),
            new { query.CountNumber, query.BoxSearch, query.Location, UpperRowId = upper,
                BeforeRowId = cursor?.BeforeRowId, Take = size + 1 },
            cancellationToken: cancellationToken))).ToArray();
        var items = rows.Take(size).Select(x => new CountItemDto(
            x.DetailId,x.CountNumber,x.BoxNumber,x.LocationCode,x.ItemCode,x.LotNumber,
            x.PackageUnit,x.QuantityPcu,x.QuantityStu,x.CreatedAt)
        {
            UpdatedAt=x.UpdatedAt, LastCorrectionOldQuantity=x.LastCorrectionOldQuantity,
            LastCorrectionNewQuantity=x.LastCorrectionNewQuantity, LastCorrectionAtUtc=x.LastCorrectionAtUtc
        }).ToArray();
        var next = rows.Length > size
            ? new HistoryCursor(upper, items[^1].DetailId).Encode(query) : null;
        return new CountHistoryPage(environment.Name, items, next, summary?.TotalCount, summary?.FilteredCount);
    }

    private sealed class HistorySummary
    {
        public string? UpperRowId { get; set; }
        public long TotalCount { get; set; }
        public long FilteredCount { get; set; }
    }

    private InventoryCountDto ToDto(CountRow row, SelectedEnvironment environment)
    {
        var isTest = environment.Name == "Test";
        var reviewed = isTest ? testSchemaReviewed : productionSchemaReviewed;
        var switchedOn = isTest ? enableTestWrites : enableProductionWrites;
        var reason = CountWritePolicy.CountSubmissionBlockReason(isTest, switchedOn, reviewed);
        var enabled = reason is null;
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
        public DateTime? UpdatedAt { get; set; }
        public decimal? LastCorrectionOldQuantity { get; set; }
        public decimal? LastCorrectionNewQuantity { get; set; }
        public DateTime? LastCorrectionAtUtc { get; set; }
    }
}
