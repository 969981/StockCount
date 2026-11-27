using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Infrastructure;

public sealed class CountWriteException(int statusCode, string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed record CountWriteOptions(bool EnableTestWrites, bool EnableProductionWrites,
    string AuditUser, bool TestSchemaReviewed, bool ProductionSchemaReviewed,
    LocationValidationOptions? LocationValidation = null,
    bool TestQuantityUpdatesReviewed = false, bool ProductionQuantityUpdatesReviewed = false)
{
    public bool IsEnabled(SelectedEnvironment environment) =>
        environment.Name == "Test" ? EnableTestWrites :
        environment.Name == "Production" && EnableProductionWrites;

    public bool IsCorrectionEnabled(SelectedEnvironment environment) => IsEnabled(environment) && IsReviewed(environment) &&
        (environment.Name == "Test" ? TestQuantityUpdatesReviewed : environment.Name == "Production" && ProductionQuantityUpdatesReviewed);

    public bool IsReviewed(SelectedEnvironment environment) =>
        environment.Name == "Test" ? TestSchemaReviewed :
        environment.Name == "Production" && ProductionSchemaReviewed;
}

public interface IInventoryWriteRepository
{
    Task<SaveCountItemResult> SubmitAsync(SelectedEnvironment environment, string countNumber,
        Guid requestId, SaveCountItemRequest request, CancellationToken cancellationToken);

    Task<SaveCountItemResult?> GetSubmissionAsync(SelectedEnvironment environment,
        string countNumber, Guid requestId, CancellationToken cancellationToken);
}

public interface IInventoryCorrectionRepository
{
    Task<CountBoxDecision> InspectCorrectionAsync(SelectedEnvironment environment, string countNumber,
        string location, string box, string item, string lot, string unit, CancellationToken ct);
    Task<SaveCountItemResult> UpdateQuantityAsync(SelectedEnvironment environment, string countNumber,
        string detailId, Guid requestId, UpdateCountItemRequest request, CancellationToken ct);
}

/// <summary>
/// Insert exactly one XCOUTD row in a serializable transaction.
/// New StockCount rows use their request UUID as AUUID_0; no auxiliary ERP table.
/// Mutations are explicitly gated; no production writes by default.
/// </summary>
public sealed class SqlInventoryWriteRepository(string connectionString, CountWriteOptions options)
    : IInventoryWriteRepository, IInventoryCorrectionRepository
{
    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new CountWriteException(503, "DATABASE_UNAVAILABLE", "SQL Server 尚未配置。");
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<SaveCountItemResult?> GetSubmissionAsync(
        SelectedEnvironment environment, string countNumber, Guid requestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(countNumber) || countNumber.Length > 30 || requestId == Guid.Empty)
            throw new CountWriteException(400, "SUBMISSION_REQUEST_INVALID", "盘点单号或请求标识无效。");

        var schema = EnvironmentSelector.TablePrefix(environment);
        await using var connection = await OpenAsync(ct);
        // Read ledger and native UUID under one isolation boundary; a concurrent
        // correction cannot move the receipt between the two queries.
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        if (options.IsCorrectionEnabled(environment) && !await HasLedgerAsync(connection,tx,schema,ct))
            throw new CountWriteException(503,"SUBMISSION_LOOKUP_UNAVAILABLE","更新流水不可用，无法确认原请求。");
        var row = await FindRequestAsync(connection, tx, schema, countNumber, requestId, ct);
        await tx.CommitAsync(ct);
        return row is null ? null : ToResult(row, environment.Name, countNumber, requestId, false);
    }

    public async Task<SaveCountItemResult> SubmitAsync(SelectedEnvironment environment,
        string countNumber, Guid requestId, SaveCountItemRequest request, CancellationToken ct)
    {
        if (requestId == Guid.Empty)
            throw new CountWriteException(400, "IDEMPOTENCY_KEY_REQUIRED", "提交必须提供非空 UUID 幂等键。");
        if (request is null)
            throw new CountWriteException(400, "COUNT_REQUEST_INVALID", "请填写本次实盘信息。");

        if (!request.CountedQtyStu.HasValue)
            throw new CountWriteException(400, "COUNT_QUANTITY_REQUIRED", "箱内数量为必填项，请人工输入。");

        var validation = CountWritePolicy.Validate(countNumber, request.RawQrCode,
            request.BoxNumber, request.CountedLocation, request.CountedQtyStu.Value);
        if (validation is not null)
            throw new CountWriteException(400, validation, validation switch
            {
                "BOX_NUMBER_TOO_LONG" => "箱号无法写入 XCOUTD.BOXNUM_0（最大20字符）。",
                "LOCATION_INVALID" => "库位不能为空且不可超过10字符。",
                "COUNT_QUANTITY_INVALID" => "实盘数量必须非负，且符合 numeric(28,13) 精度。",
                _ => "提交信息无效，请核对盘点单和二维码。"
            });

        if (environment.Name == "Production" && !options.EnableProductionWrites)
            throw new CountWriteException(403, "PRODUCTION_WRITE_DISABLED", "生产写入未获授权，服务端开关关闭。");
        if (environment.Name == "Test" && !options.EnableTestWrites)
            throw new CountWriteException(503, "ERP_WRITE_UNAVAILABLE", "测试环境写入尚未由管理员启用。");
        if (!options.IsReviewed(environment))
            throw new CountWriteException(503, "SCHEMA_NOT_APPROVED",
                $"必须先独立验证 {environment.Name} 对应的 X3 字段/单位/触发器，再为该帐套确认结构审查。");

        if (string.IsNullOrWhiteSpace(options.AuditUser) || options.AuditUser.Length > 5 ||
            options.AuditUser.Any(char.IsControl))
            throw new CountWriteException(503, "AUDIT_USER_NOT_CONFIGURED",
                "需要配置1至5字符的专用 X3 审计用户编码。");

        var schema = EnvironmentSelector.TablePrefix(environment);

        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var stage = "获取盘点事务锁";
        try
        {
            // Protect the same box across all counts and separate API processes.
            // The application lock is scoped to this SQL transaction.
            const string lockSql = """
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock
                    @Resource=@LockResource, @LockMode=N'Exclusive',
                    @LockOwner=N'Transaction', @LockTimeout=10000;
                SELECT @result;
                """;
            var lockResource = BoxDuplicateQuery.LockResource(environment, request.BoxNumber);
            var acquired = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(lockSql, new { LockResource=lockResource },
                    transaction, cancellationToken:ct));
            if (acquired < 0)
                throw new CountWriteException(409, "COUNT_BUSY",
                    "当前箱码有其他设备正在提交，请稍后以相同请求ID重试。");

            // Recover only rows created by this app with the same native UUID.
            stage = "查询 XCOUTD 原请求记录";
            if (options.IsCorrectionEnabled(environment) && !await HasLedgerAsync(connection,transaction,schema,ct))
                throw new CountWriteException(503,"UPDATE_LEDGER_MISSING","已启用更新但流水表不可用，暂停写入。");
            var existing = await FindRequestAsync(connection, transaction, schema, countNumber, requestId, ct);
            if (existing is not null)
            {
                if (existing.Operation != "Insert" || !string.Equals(existing.BoxNumber, request.BoxNumber, StringComparison.Ordinal) ||
                    !string.Equals(existing.LocationCode, request.CountedLocation, StringComparison.Ordinal) ||
                    existing.SavedQtyStu != request.CountedQtyStu.Value ||
                    existing.SavedQtyPcu != request.CountedQtyStu.Value)
                    throw new CountWriteException(409, "IDEMPOTENCY_PAYLOAD_MISMATCH",
                        "同一请求ID对应的盘点箱号、库位或数量不一致，请核查盘点明细。");
                await transaction.CommitAsync(ct);
                return ToResult(existing, environment.Name, countNumber, requestId, true);
            }

            // Do not guess how Sage X3 creates ROWID. An identity-managed ROWID
            // is a prerequisite for this insertion adapter; fail closed otherwise.
            stage = "检查 XCOUTD.ROWID";
            var identityCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("""
                    SELECT COUNT(*) FROM sys.columns C
                    WHERE C.object_id=OBJECT_ID(@TableName) AND C.name=N'ROWID'
                      AND C.is_identity=1;
                    """, new { TableName=schema+".[XCOUTD]" }, transaction, cancellationToken:ct));
            if (identityCount != 1)
                throw new CountWriteException(503, "ERP_SCHEMA_UNSUPPORTED",
                    "XCOUTD.ROWID 不是已验证的数据库自增列，不允许直接插入。");

            stage = "读取 XCOUTH 盘点单";
            var headerSql = $"""
                SELECT H.STOFCY_0 AS SiteCode,H.WRH_0 AS WarehouseCode
                FROM {schema}.[XCOUTH] H WITH (UPDLOCK,HOLDLOCK)
                WHERE H.CUNNUM_0=@CountNumber;
                """;
            var header = await connection.QuerySingleOrDefaultAsync<HeaderRow>(
                new CommandDefinition(headerSql, new { CountNumber=countNumber },
                    transaction, cancellationToken:ct));
            if (header is null)
                throw new CountWriteException(404, "COUNT_NOT_FOUND", "盘点单不存在。");
            // All existing header statuses are permitted by the 2026-11-25 business decision.
            // The locked header still provides existence and site/warehouse context.

            stage = "核验盘点库位";
            var scope = (options.LocationValidation ?? new LocationValidationOptions()).For(environment.Name);
            var matches = await connection.QuerySingleAsync<LocationLookup>(
                new CommandDefinition(LocationLookupQuery.Sql(schema),
                    LocationLookupQuery.Parameters(scope, header.SiteCode, header.WarehouseCode, request.CountedLocation),
                    transaction, cancellationToken:ct));
            var locationError = scope.Classify(matches.TotalMatches, matches.ScopedMatches);
            if (locationError is not null)
                throw new CountWriteException(locationError == "LOCATION_NOT_FOUND" ? 404 :
                    locationError == "LOCATION_AMBIGUOUS" ? 409 : 422, locationError, scope.Describe(locationError));

            stage = "读取 XLABELDTL 箱标签";
            var labelSql = $"""
                SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
                       L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit
                FROM {schema}.[XLABELDTL] L
                """;
            async Task<LabelForWrite[]> QueryLabelAsync(string predicate, string value) =>
                (await connection.QueryAsync<LabelForWrite>(
                    new CommandDefinition(labelSql + predicate, new { Value=value },
                        transaction, cancellationToken:ct))).ToArray();

            var labels = await LabelLookupPolicy.FindAsync(request.RawQrCode,
                value => QueryLabelAsync(" WHERE L.QRCOD_0=@Value;", value),
                value => QueryLabelAsync(" WHERE L.PID_0=@Value;", value));
            if (labels.Length > 1)
                throw new CountWriteException(409, "LABEL_AMBIGUOUS",
                    "二维码或箱号对应多条标签记录，请联系管理员核查。");
            if (labels.Length == 0)
                throw new CountWriteException(404, "LABEL_NOT_FOUND",
                    "完整二维码及箱号均未查询到标签。");
            var label = labels[0];
            if (!string.Equals(label.BoxNumber, request.BoxNumber, StringComparison.Ordinal))
                throw new CountWriteException(409, "BOX_MISMATCH",
                    "请求箱号与数据库标签箱号不一致，请重新扫码。");
            if (label.ItemCode.Length is < 1 or > 20 || label.LotNumber.Length > 30)
                throw new CountWriteException(422, "ERP_FIELD_TOO_LONG",
                    "标签的物料编码或批次无法写入 XCOUTD。");

            // Count in STU. The label's packaging unit and factor are irrelevant:
            // XCOUTD.PCU_0 = STU, factor = 1, both quantity columns = manual count.
            if (!CountWritePolicy.IsValidStockUnit(label.StockUnit))
                throw new CountWriteException(409, "UNIT_MAPPING_UNSUPPORTED",
                    "标签存货单位为空、包含控制字符或超过3字符，无法写入盘点明细。");

            stage = "检查当前帐套 XCOUTD 全部盘点单重复箱号";
            var duplicate = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(BoxDuplicateQuery.LockedExists(environment),
                    new { BoxNumber=label.BoxNumber },
                    transaction, cancellationToken:ct));
            if (duplicate)
                throw new CountWriteException(409, "BOX_ALREADY_RECORDED", "该箱号已扫描！");

            var units = CountWritePolicy.MapCountUnits(label.StockUnit, request.CountedQtyStu.Value);
            var qty = units.QuantityStu;
            // Every mandatory XCOUTD field is assigned explicitly except
            // ROWID, which the preflight above requires SQL Server IDENTITY to generate.
            // Unmapped optional business strings are blank (not SQL NULL).
            stage = "插入 XCOUTD 盘点明细";
            var insertSql = $"""
                INSERT INTO {schema}.[XCOUTD]
                    (UPDTICK_0,CUNNUM_0,PALNUM_0,BOXNUM_0,ITMREF_0,
                     LOT_0,LOC_0,ECCVALMAJ_0,PCU_0,QTYPCU_0,PCUSTUCOE_0,
                     QTYSTU_0,PDAID_0,CREDATTIM_0,UPDDATTIM_0,AUUID_0,
                     CREUSR_0,UPDUSR_0)
                OUTPUT CONVERT(nvarchar(40),INSERTED.ROWID)
                VALUES
                    (1,@CountNumber,N'',@BoxNumber,@ItemCode,
                     @LotNumber,@Location,N'',@StockUnit,@Quantity,@UnitFactor,
                     @Quantity,N'StockCount',GETDATE(),GETDATE(),
                     CONVERT(binary(16),@RequestId),@AuditUser,@AuditUser);
                """;
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@CountNumber", SqlDbType.NVarChar, 30).Value=countNumber;
            insert.Parameters.Add("@BoxNumber", SqlDbType.NVarChar, 20).Value=label.BoxNumber;
            insert.Parameters.Add("@ItemCode", SqlDbType.NVarChar, 20).Value=label.ItemCode;
            insert.Parameters.Add("@LotNumber", SqlDbType.NVarChar, 30).Value=label.LotNumber;
            insert.Parameters.Add("@Location", SqlDbType.NVarChar, 10).Value=request.CountedLocation;
            insert.Parameters.Add("@StockUnit", SqlDbType.NVarChar, 3).Value=units.PackageUnit;
            insert.Parameters.Add("@AuditUser", SqlDbType.NVarChar, 5).Value=options.AuditUser;
            insert.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value=requestId;
            var amount = insert.Parameters.Add("@Quantity", SqlDbType.Decimal);
            amount.Precision=28;
            amount.Scale=13;
            amount.Value=qty;
            var ratio = insert.Parameters.Add("@UnitFactor", SqlDbType.Decimal);
            ratio.Precision=18;
            ratio.Scale=7;
            ratio.Value=units.UnitFactor;
            var detailId = (string?)await insert.ExecuteScalarAsync(ct);
            if (string.IsNullOrWhiteSpace(detailId))
                throw new CountWriteException(503, "ERP_WRITE_FAILED", "XCOUTD 未返回新建 ROWID。");

            // New inserts also receive an immutable receipt when the migration is deployed.
            // Legacy deployments without the ledger retain the native UUID behavior.
            if (await HasLedgerAsync(connection,transaction,schema,ct))
                await connection.ExecuteAsync(new CommandDefinition($"""
                    INSERT INTO {schema}.[StockCountOperationReceipt]
                        (CountNumber,RequestId,Operation,PayloadHash,DetailId,BoxNumber,LocationCode,StockUnit,
                         OldQtyStu,OldQtyPcu,SavedQtyStu,SavedQtyPcu,AuditUser)
                    VALUES (@Count,@RequestId,N'Insert',NULL,@DetailId,@BoxNumber,@Location,@StockUnit,
                        NULL,NULL,@Qty,@Qty,@Audit);
                    """,new { Count=countNumber,RequestId=requestId,DetailId=detailId,label.BoxNumber,
                        Location=request.CountedLocation,StockUnit=units.PackageUnit,Qty=qty,Audit=options.AuditUser },
                    transaction,cancellationToken:ct));
            stage = "提交 SQL 事务";
            await transaction.CommitAsync(ct);
            return new SaveCountItemResult(environment.Name,countNumber,label.BoxNumber,
                detailId,units.QuantityStu,units.QuantityPcu,units.PackageUnit,requestId,"Committed",false) { LocationCode=request.CountedLocation };
        }
        catch (CountWriteException error)
        {
            var rolledBack = await SafeRollbackAsync(transaction);
            if (!rolledBack && error.Code != "SUBMISSION_OUTCOME_UNKNOWN")
                throw new CountWriteException(503, "SUBMISSION_OUTCOME_UNKNOWN",
                    "提交异常且回滚未确认，请查询原请求ID。", error);
            throw;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await SafeRollbackAsync(transaction);
            throw new CountWriteException(409, "IDEMPOTENCY_WRITE_CONFLICT",
                "写入唯一记录发生冲突，请通过原 Idempotency-Key 查询已有提交结果，不要换新请求ID。", error);
        }
        catch (SqlException error) when (error.Number is 208 or 207 or 229 or 515 or 544 or 8101)
        {
            var rolledBack = await SafeRollbackAsync(transaction);
            if (!rolledBack)
                throw new CountWriteException(503, "SUBMISSION_OUTCOME_UNKNOWN",
                    "SQL 写入异常且回滚未确认，请查询原请求ID。", error);
            throw new CountWriteException(503, "ERP_SCHEMA_UNAVAILABLE",
                $"SQL Server 写入失败（{stage}，SQL 错误 {error.Number}）；事务已回滚，请联系管理员查看后台日志。",
                error);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    // Use the same SQL Server binary conversion as the INSERT. Never equate
    // another device's row with this UUID merely because its box number matches.
    private static async Task<SubmissionRow?> FindRequestAsync(SqlConnection connection,
        SqlTransaction? transaction, string schema, string countNumber, Guid requestId, CancellationToken ct)
    {
        var receipt = await FindReceiptAsync(connection, transaction, schema, countNumber, requestId, ct);
        if (receipt is not null) return receipt;
        var hint = transaction is null ? "" : " WITH (UPDLOCK,HOLDLOCK)";
        var sql = $"""
            SELECT TOP (2) D.BOXNUM_0 AS BoxNumber,D.LOC_0 AS LocationCode,
                   CONVERT(nvarchar(40),D.ROWID) AS DetailId,
                   D.PCU_0 AS StockUnit,D.QTYSTU_0 AS SavedQtyStu,D.QTYPCU_0 AS SavedQtyPcu
            FROM {schema}.[XCOUTD] D{hint}
            WHERE D.CUNNUM_0=@CountNumber
              AND D.AUUID_0=CONVERT(binary(16),@RequestId)
              AND D.PDAID_0=N'StockCount';
            """;
        var rows = (await connection.QueryAsync<SubmissionRow>(new CommandDefinition(sql,
            new { CountNumber=countNumber, RequestId=requestId }, transaction,
            cancellationToken:ct))).ToArray();
        if (rows.Length > 1)
            throw new CountWriteException(409, "SUBMISSION_AMBIGUOUS",
                "同一请求识别码对应多条盘点明细，请管理员核查。");
        return rows.SingleOrDefault();
    }

    private static SaveCountItemResult ToResult(SubmissionRow row, string environment,
        string countNumber, Guid requestId, bool replayed) =>
        new(environment,countNumber,row.BoxNumber,row.DetailId,
            row.SavedQtyStu,row.SavedQtyPcu,row.StockUnit,requestId,"Committed",replayed) { Operation = row.Operation, LocationCode=row.LocationCode };

    private static async Task<bool> SafeRollbackAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(CancellationToken.None); return true; }
        catch (InvalidOperationException) { return false; }
        catch (SqlException) { return false; }
    }

    private sealed class HeaderRow
    {
        public string SiteCode { get; set; } = "";
        public string WarehouseCode { get; set; } = "";
    }

    private sealed class LabelForWrite
    {
        public string BoxNumber { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string StockUnit { get; set; } = "";
    }

    private sealed class SubmissionRow
    {
        public string Operation { get; set; } = "Insert";
        public string? PayloadHash { get; set; }
        public string BoxNumber { get; set; } = "";
        public string LocationCode { get; set; } = "";
        public string DetailId { get; set; } = "";
        public string StockUnit { get; set; } = "";
        public decimal SavedQtyStu { get; set; }
        public decimal SavedQtyPcu { get; set; }
    }

    // The ledger is optional for legacy inserts, mandatory for corrections.
    // Its immutable receipts survive subsequent changes to XCOUTD quantities.
    private static async Task<bool> HasLedgerAsync(SqlConnection db, SqlTransaction? tx, string schema, CancellationToken ct) =>
        await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN OBJECT_ID(@Name,N'U') IS NULL THEN 0 ELSE 1 END;",
            new { Name = schema + ".[StockCountOperationReceipt]" }, tx, cancellationToken:ct)) == 1;

    private static async Task<SubmissionRow?> FindReceiptAsync(SqlConnection db, SqlTransaction? tx,
        string schema, string count, Guid requestId, CancellationToken ct)
    {
        if (!await HasLedgerAsync(db, tx, schema, ct)) return null;
        var hint = tx is null ? "" : " WITH (UPDLOCK,HOLDLOCK)";
        return await db.QuerySingleOrDefaultAsync<SubmissionRow>(new CommandDefinition($"""
            SELECT BoxNumber, LocationCode, DetailId, StockUnit,
                   SavedQtyStu, SavedQtyPcu, Operation, PayloadHash
            FROM {schema}.[StockCountOperationReceipt]{hint}
            WHERE CountNumber=@Count AND RequestId=@RequestId;
            """, new { Count=count, RequestId=requestId }, tx, cancellationToken:ct));
    }

    private sealed class CorrectionRow
    {
        public string DetailId { get; set; } = "";
        public string CountNumber { get; set; } = "";
        public string BoxNumber { get; set; } = "";
        public string LocationCode { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string StockUnit { get; set; } = "";
        public decimal QuantityStu { get; set; }
        public decimal QuantityPcu { get; set; }
        public decimal UnitFactor { get; set; }
        public int Tick { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string PdaId { get; set; } = "";
        public Guid NativeUuid { get; set; }
        public string PalletNumber { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
        public string EccMajor { get; set; } = "";

        public bool Compatible(string item, string lot, string unit) =>
            string.Equals(ItemCode,item,StringComparison.Ordinal) &&
            string.Equals(LotNumber,lot,StringComparison.Ordinal) &&
            string.Equals(StockUnit,unit,StringComparison.Ordinal) &&
            CountWritePolicy.IsValidStockUnit(unit) && UnitFactor == 1m &&
            QuantityStu == QuantityPcu && QuantityStu >= 0 && Tick >= 0 && Tick < int.MaxValue;

        public string Version => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                DetailId,CountNumber,BoxNumber,LocationCode,ItemCode,LotNumber,StockUnit,
                QuantityStu,QuantityPcu,UnitFactor,Tick,UpdatedAt,PdaId,NativeUuid,PalletNumber,CreatedAt,CreatedBy,EccMajor
            })));
        public RecordedCountItem Snapshot => new(DetailId,CountNumber,BoxNumber,LocationCode,
            QuantityStu,QuantityPcu,StockUnit,UpdatedAt,Version);
    }

    private static async Task<CorrectionRow[]> FindBoxRowsAsync(SqlConnection db, SqlTransaction? tx,
        string schema, string box, CancellationToken ct)
    {
        var hint = tx is null ? "" : " WITH (UPDLOCK,HOLDLOCK)";
        return (await db.QueryAsync<CorrectionRow>(new CommandDefinition($"""
            SELECT TOP (2) CONVERT(nvarchar(40),D.ROWID) AS DetailId,
                   D.CUNNUM_0 AS CountNumber,D.BOXNUM_0 AS BoxNumber,D.LOC_0 AS LocationCode,
                   D.ITMREF_0 AS ItemCode,D.LOT_0 AS LotNumber,D.PCU_0 AS StockUnit,
                   D.QTYSTU_0 AS QuantityStu,D.QTYPCU_0 AS QuantityPcu,D.PCUSTUCOE_0 AS UnitFactor,
                   D.UPDTICK_0 AS Tick,D.UPDDATTIM_0 AS UpdatedAt,D.PDAID_0 AS PdaId,
                   CONVERT(uniqueidentifier,D.AUUID_0) AS NativeUuid,D.PALNUM_0 AS PalletNumber,
                   D.CREDATTIM_0 AS CreatedAt,D.CREUSR_0 AS CreatedBy,D.ECCVALMAJ_0 AS EccMajor
            FROM {schema}.[XCOUTD] D{hint}
            WHERE D.BOXNUM_0=@Box;
            """, new { Box=box }, tx, cancellationToken:ct))).ToArray();
    }

    private static CountBoxDecision Decide(CorrectionRow[] rows, string count, string location,
        string box, string item, string lot, string unit, bool enabled)
    {
        var row = rows.Length == 1 ? rows[0] : null;
        var mode = CorrectionPolicy.Classify(rows.Length,
            row is not null && string.Equals(row.CountNumber,count,StringComparison.Ordinal),
            row is not null && string.Equals(row.LocationCode,location,StringComparison.Ordinal),
            row is not null && string.Equals(row.BoxNumber,box,StringComparison.Ordinal) && row.Compatible(item,lot,unit),
            enabled);
        var message = mode switch
        {
            "LocationConflict" => $"该箱码已在 {row!.LocationCode} 库位盘点！",
            "CountConflict" => $"该箱码已在盘点单 {row!.CountNumber}、库位 {row.LocationCode} 提交！",
            "Ambiguous" => "该箱码存在多条历史明细，请联系管理员核查。",
            "Unsupported" => "原记录的物料、批次、单位或版本不兼容，禁止直接更新。",
            "UpdateDisabled" => "原箱已盘点，但当前帐套数量更新尚未获准。",
            _ => null
        };
        return new CountBoxDecision(mode,row?.Snapshot,message);
    }

    public async Task<CountBoxDecision> InspectCorrectionAsync(SelectedEnvironment environment,
        string countNumber, string location, string box, string item, string lot, string unit, CancellationToken ct)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        await using var db = await OpenAsync(ct);
        var rows = await FindBoxRowsAsync(db,null,schema,box,ct);
        return Decide(rows,countNumber,location,box,item,lot,unit,
            options.IsCorrectionEnabled(environment) && await HasLedgerAsync(db,null,schema,ct));
    }

    public async Task<SaveCountItemResult> UpdateQuantityAsync(SelectedEnvironment environment,
        string countNumber, string detailId, Guid requestId, UpdateCountItemRequest request, CancellationToken ct)
    {
        if (requestId == Guid.Empty || !HistoryCursor.IsRowId(detailId) ||
            !CorrectionPolicy.ValidVersion(request.ExpectedVersion) || !request.CountedQtyStu.HasValue)
            throw new CountWriteException(400,"UPDATE_REQUEST_INVALID","更新请求、数量或版本无效，请重新扫码。");
        var qty = request.CountedQtyStu.Value;
        var validation = CountWritePolicy.Validate(countNumber,request.RawQrCode,request.BoxNumber,request.CountedLocation,qty);
        if (validation is not null) throw new CountWriteException(400,validation,"更新请求数据无效。");
        if (!options.IsCorrectionEnabled(environment))
            throw new CountWriteException(403,"UPDATE_DISABLED","当前帐套数量更新未经过独立授权及结构审核。");
        if (string.IsNullOrWhiteSpace(options.AuditUser) || options.AuditUser.Length > 5 || options.AuditUser.Any(char.IsControl))
            throw new CountWriteException(503,"AUDIT_USER_NOT_CONFIGURED","请配置专用 X3 审计用户编码。");

        var schema = EnvironmentSelector.TablePrefix(environment);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
                environment.Name,countNumber,detailId,request.RawQrCode,request.BoxNumber,
                request.CountedLocation, Quantity = qty.ToString("0.############################",CultureInfo.InvariantCulture),
                request.ExpectedVersion
            })));
        await using var db = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var locked = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',
                    @LockOwner=N'Transaction',@LockTimeout=10000;
                SELECT @r;
                """, new { Resource=BoxDuplicateQuery.LockResource(environment,request.BoxNumber) },tx,cancellationToken:ct));
            if (locked < 0) throw new CountWriteException(409,"COUNT_BUSY","箱码正在被其他设备处理，请保留原请求重试。");
            if (!await HasLedgerAsync(db,tx,schema,ct))
                throw new CountWriteException(503,"UPDATE_LEDGER_MISSING","尚未部署并核验数量更新操作流水表。");
            var previous = await FindRequestAsync(db,tx,schema,countNumber,requestId,ct);
            if (previous is not null)
            {
                if (previous.Operation != "Update" || previous.PayloadHash != fingerprint)
                    throw new CountWriteException(409,"IDEMPOTENCY_PAYLOAD_MISMATCH","原请求标识对应不同操作或数据。");
                await tx.CommitAsync(ct);
                return ToResult(previous,environment.Name,countNumber,requestId,true);
            }

            var header = await db.QuerySingleOrDefaultAsync<HeaderRow>(new CommandDefinition($"""
                SELECT STOFCY_0 AS SiteCode,WRH_0 AS WarehouseCode
                FROM {schema}.[XCOUTH] WITH (UPDLOCK,HOLDLOCK) WHERE CUNNUM_0=@Count;
                """,new { Count=countNumber },tx,cancellationToken:ct));
            if (header is null) throw new CountWriteException(404,"COUNT_NOT_FOUND","盘点单不存在。");
            var scope = (options.LocationValidation ?? new LocationValidationOptions()).For(environment.Name);
            var matches = await db.QuerySingleAsync<LocationLookup>(new CommandDefinition(LocationLookupQuery.Sql(schema),
                LocationLookupQuery.Parameters(scope,header.SiteCode,header.WarehouseCode,request.CountedLocation),
                tx,cancellationToken:ct));
            if (scope.Classify(matches.TotalMatches,matches.ScopedMatches) is { } locationError)
                throw new CountWriteException(422,locationError,scope.Describe(locationError));

            var labelSql = $"""
                SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit
                FROM {schema}.[XLABELDTL] L
                """;
            async Task<LabelForWrite[]> ReadLabel(string predicate,string value) =>
                (await db.QueryAsync<LabelForWrite>(new CommandDefinition(labelSql+predicate,new { Value=value },tx,cancellationToken:ct))).ToArray();
            var labels = await LabelLookupPolicy.FindAsync(request.RawQrCode,
                value=>ReadLabel(" WHERE L.QRCOD_0=@Value;",value),
                value=>ReadLabel(" WHERE L.PID_0=@Value;",value));
            if (labels.Length != 1 || !string.Equals(labels[0].BoxNumber,request.BoxNumber,StringComparison.Ordinal))
                throw new CountWriteException(409,"UPDATE_RECORD_CONFLICT","箱标签已变化或存在歧义，请重新扫码。");
            var label = labels[0];
            var rows = await FindBoxRowsAsync(db,tx,schema,label.BoxNumber,ct);
            var decision = Decide(rows,countNumber,request.CountedLocation,label.BoxNumber,label.ItemCode,label.LotNumber,label.StockUnit,true);
            if (decision.Mode != "Update" || rows[0].DetailId != detailId)
                throw new CountWriteException(409,"UPDATE_RECORD_CONFLICT",decision.Message ?? "原明细已变化，请重新扫码。");
            var row = rows[0];
            if (row.Version != request.ExpectedVersion)
                throw new CountWriteException(409,"UPDATE_VERSION_CONFLICT","其他设备已修改该箱，请重新扫码后输入数量。");

            // Archive the native StockCount insertion receipt before the first mutation.
            // Legacy UUIDs not created by StockCount are not adopted as request IDs.
            if (row.PdaId == "StockCount" && row.NativeUuid != Guid.Empty)
            {
                var original = await FindRequestAsync(db,tx,schema,countNumber,row.NativeUuid,ct);
                if (original is null || original.Operation != "Insert" || original.DetailId != row.DetailId)
                    throw new CountWriteException(409,"UPDATE_RECORD_CONFLICT","原提交标识有歧义，禁止更新。");
                if (await FindReceiptAsync(db,tx,schema,countNumber,row.NativeUuid,ct) is null)
                    await db.ExecuteAsync(new CommandDefinition($"""
                        INSERT INTO {schema}.[StockCountOperationReceipt]
                            (CountNumber,RequestId,Operation,PayloadHash,DetailId,BoxNumber,LocationCode,StockUnit,
                             OldQtyStu,OldQtyPcu,SavedQtyStu,SavedQtyPcu,AuditUser)
                        VALUES (@Count,@Id,N'Insert',NULL,@DetailId,@BoxNumber,@LocationCode,@StockUnit,
                             NULL,NULL,@QuantityStu,@QuantityPcu,@Audit);
                        """,new { Count=countNumber,Id=row.NativeUuid,row.DetailId,row.BoxNumber,row.LocationCode,
                            row.StockUnit,row.QuantityStu,row.QuantityPcu,Audit=row.CreatedBy },tx,cancellationToken:ct));
            }
            // Explicitly approved X3 tick increment. Original ROWID/AUUID, location, pallet and creation fields untouched.
            var updated = await db.ExecuteAsync(new CommandDefinition($"""
                UPDATE {schema}.[XCOUTD]
                SET QTYSTU_0=@Qty,QTYPCU_0=@Qty,UPDDATTIM_0=GETDATE(),UPDUSR_0=@Audit,
                    UPDTICK_0=UPDTICK_0+1
                WHERE ROWID=CONVERT(numeric(38,0),@DetailId) AND CUNNUM_0=@Count
                    AND BOXNUM_0=@BoxNumber AND LOC_0=@LocationCode AND UPDTICK_0=@Tick
                    AND QTYSTU_0=@QuantityStu AND QTYPCU_0=@QuantityPcu AND PCUSTUCOE_0=1;
                """,new { Qty=qty,Audit=options.AuditUser,row.DetailId,Count=countNumber,row.BoxNumber,row.LocationCode,
                    row.Tick,row.QuantityStu,row.QuantityPcu },tx,cancellationToken:ct));
            if (updated != 1) throw new CountWriteException(409,"UPDATE_VERSION_CONFLICT","原明细已变化，请重新扫码。");
            var after = await FindBoxRowsAsync(db,tx,schema,label.BoxNumber,ct);
            if (after.Length != 1 || after[0].DetailId != row.DetailId || after[0].QuantityStu != qty ||
                after[0].QuantityPcu != qty || after[0].NativeUuid != row.NativeUuid ||
                after[0].CountNumber != row.CountNumber || after[0].LocationCode != row.LocationCode ||
                after[0].PalletNumber != row.PalletNumber || after[0].CreatedAt != row.CreatedAt ||
                after[0].CreatedBy != row.CreatedBy || after[0].EccMajor != row.EccMajor ||
                after[0].PdaId != row.PdaId || after[0].BoxNumber != row.BoxNumber ||
                after[0].Tick != row.Tick + 1 || !after[0].Compatible(label.ItemCode,label.LotNumber,label.StockUnit))
                throw new CountWriteException(503,"UPDATE_POSTCHECK_FAILED","更新后核验失败，已拒绝事务，请管理员核查。");
            await db.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO {schema}.[StockCountOperationReceipt]
                    (CountNumber,RequestId,Operation,PayloadHash,DetailId,BoxNumber,LocationCode,StockUnit,
                     OldQtyStu,OldQtyPcu,SavedQtyStu,SavedQtyPcu,AuditUser)
                VALUES (@Count,@RequestId,N'Update',@Fingerprint,@DetailId,@BoxNumber,@LocationCode,@StockUnit,
                     @QuantityStu,@QuantityPcu,@Qty,@Qty,@Audit);
                """,new { Count=countNumber,RequestId=requestId,Fingerprint=fingerprint,row.DetailId,row.BoxNumber,
                    row.LocationCode,row.StockUnit,row.QuantityStu,row.QuantityPcu,Qty=qty,Audit=options.AuditUser },
                tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return new SaveCountItemResult(environment.Name,countNumber,row.BoxNumber,row.DetailId,qty,qty,
                row.StockUnit,requestId,"Committed",false) { Operation="Update", LocationCode=row.LocationCode };
        }
        catch (CountWriteException error)
        {
            if (!await SafeRollbackAsync(tx))
                throw new CountWriteException(503,"SUBMISSION_OUTCOME_UNKNOWN","回滚未确认，请查询原请求 UUID。",error);
            throw;
        }
        catch (Exception error)
        {
            await SafeRollbackAsync(tx);
            // Even a failed commit/connection may have committed; keep original request.
            throw new CountWriteException(503,"SUBMISSION_OUTCOME_UNKNOWN","更新结果待确认，请查询原请求 UUID。",error);
        }
    }

}
