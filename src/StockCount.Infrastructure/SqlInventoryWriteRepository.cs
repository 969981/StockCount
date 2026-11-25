using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Infrastructure;

public sealed class CountWriteException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed record CountWriteOptions(bool EnableTestWrites, bool EnableProductionWrites,
    string AuditUser, bool TestSchemaReviewed, bool ProductionSchemaReviewed,
    LocationValidationOptions? LocationValidation = null)
{
    public bool IsEnabled(SelectedEnvironment environment) =>
        environment.Name == "Test" ? EnableTestWrites :
        environment.Name == "Production" && EnableProductionWrites;

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

/// <summary>
/// Insert exactly one XCOUTD row and one dbo.StockCountSubmission journal row
/// in the SAME SQL Server serializable transaction. Idempotency is durable.
/// Mutations are explicitly gated; no production writes by default.
/// </summary>
public sealed class SqlInventoryWriteRepository(string connectionString, CountWriteOptions options)
    : IInventoryWriteRepository
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

        const string query = """
            SELECT S.Environment,S.CountNumber,S.RequestId,S.BoxNumber,
                   CONVERT(nvarchar(40),S.DetailRowId) AS DetailId,
                   S.StockUnit,S.QuantityStu AS SavedQtyStu,S.QuantityPcu AS SavedQtyPcu
            FROM [dbo].[StockCountSubmission] S
            WHERE S.Environment=@Environment AND S.CountNumber=@CountNumber
              AND S.RequestId=@RequestId;
            """;
        await using var connection = await OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<SubmissionRow>(
            new CommandDefinition(query,
                new { Environment=environment.Name, CountNumber=countNumber, RequestId=requestId },
                cancellationToken:ct));
        return row is null ? null : ToResult(row, false);
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
        var payloadHash = CountWritePolicy.PayloadHash(environment.Name, countNumber,
            request.RawQrCode, request.BoxNumber, request.CountedLocation, request.CountedQtyStu.Value);

        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            // Protect the same count+box across separate API processes.
            // The application lock is scoped to this SQL transaction.
            const string lockSql = """
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock
                    @Resource=@LockResource, @LockMode=N'Exclusive',
                    @LockOwner=N'Transaction', @LockTimeout=10000;
                SELECT @result;
                """;
            var lockId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(environment.Name + "\u001F" +
                    countNumber + "\u001F" + request.BoxNumber)));
            var acquired = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(lockSql, new { LockResource="StockCount:"+lockId },
                    transaction, cancellationToken:ct));
            if (acquired < 0)
                throw new CountWriteException(409, "COUNT_BUSY",
                    "当前箱码有其他设备正在提交，请稍后以相同请求ID重试。");

            // Check idempotency BEFORE duplicate-box check, so a successful
            // request replay returns its original response instead of a 409.
            const string existingSql = """
                SELECT S.Environment,S.CountNumber,S.RequestId,S.BoxNumber,
                       CONVERT(nvarchar(40),S.DetailRowId) AS DetailId,
                       S.StockUnit,S.QuantityStu AS SavedQtyStu,
                       S.QuantityPcu AS SavedQtyPcu,S.PayloadHash
                FROM [dbo].[StockCountSubmission] S WITH (UPDLOCK,HOLDLOCK)
                WHERE S.Environment=@Environment
                  AND S.CountNumber=@CountNumber AND S.RequestId=@RequestId;
                """;
            var existing = await connection.QuerySingleOrDefaultAsync<SubmissionRow>(
                new CommandDefinition(existingSql,
                    new { Environment=environment.Name, CountNumber=countNumber, RequestId=requestId },
                    transaction, cancellationToken:ct));
            if (existing is not null)
            {
                if (existing.PayloadHash is null ||
                    !existing.PayloadHash.AsSpan().SequenceEqual(payloadHash))
                    throw new CountWriteException(409, "IDEMPOTENCY_PAYLOAD_MISMATCH",
                        "同一个请求ID已经用于另一组盘点数据。");
                await transaction.CommitAsync(ct);
                return ToResult(existing, true);
            }

            // Do not guess how Sage X3 creates ROWID. An identity-managed ROWID
            // is a prerequisite for this insertion adapter; fail closed otherwise.
            var identityCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("""
                    SELECT COUNT(*) FROM sys.columns C
                    WHERE C.object_id=OBJECT_ID(@TableName) AND C.name=N'ROWID'
                      AND C.is_identity=1;
                    """, new { TableName=schema+".[XCOUTD]" }, transaction, cancellationToken:ct));
            if (identityCount != 1)
                throw new CountWriteException(503, "ERP_SCHEMA_UNSUPPORTED",
                    "XCOUTD.ROWID 不是已验证的数据库自增列，不允许直接插入。");

            var headerSql = $"""
                SELECT H.STOFCY_0 AS SiteCode,H.WRH_0 AS WarehouseCode,
                       CAST(H.CUNSTA_0 AS int) AS StatusCode
                FROM {schema}.[XCOUTH] H WITH (UPDLOCK,HOLDLOCK)
                WHERE H.CUNNUM_0=@CountNumber;
                """;
            var header = await connection.QuerySingleOrDefaultAsync<HeaderRow>(
                new CommandDefinition(headerSql, new { CountNumber=countNumber },
                    transaction, cancellationToken:ct));
            if (header is null)
                throw new CountWriteException(404, "COUNT_NOT_FOUND", "盘点单不存在。");
            // Status 0 is the only currently approved write state.
            // Read-only list/label queries still show all other states.
            if (header.StatusCode != 0)
                throw new CountWriteException(409, "COUNT_NOT_WRITABLE",
                    "盘点单当前状态尚未获批准写入。");

            var scope = (options.LocationValidation ?? new LocationValidationOptions()).For(environment.Name);
            var matches = await connection.QuerySingleAsync<LocationLookup>(
                new CommandDefinition(LocationLookupQuery.Sql(schema),
                    LocationLookupQuery.Parameters(scope, header.SiteCode, header.WarehouseCode, request.CountedLocation),
                    transaction, cancellationToken:ct));
            var locationError = scope.Classify(matches.TotalMatches, matches.ScopedMatches);
            if (locationError is not null)
                throw new CountWriteException(locationError == "LOCATION_NOT_FOUND" ? 404 :
                    locationError == "LOCATION_AMBIGUOUS" ? 409 : 422, locationError, scope.Describe(locationError));

            var labelSql = $"""
                SELECT TOP (2) L.PID_0 AS BoxNumber,L.ITMREF_0 AS ItemCode,
                       L.LOT_0 AS LotNumber,L.STU_0 AS StockUnit,
                       L.PCU_0 AS PackageUnit,L.PCUSTUCOE_0 AS UnitFactor
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

            // Only a genuine 1:1 stock/package unit mapping is safe for the
            // user's forced rule QTYPCU == QTYSTU. Never silently recalculate.
            if (!CountWritePolicy.SupportedUnitMapping(
                    label.StockUnit,label.PackageUnit,label.UnitFactor))
                throw new CountWriteException(409, "UNIT_MAPPING_UNSUPPORTED",
                    "物料的包装单位/存货单位系数不是 1:1，不能在保证两数量列相等的前提下直接写入。");

            var duplicateSql = $"""
                SELECT COUNT_BIG(*) FROM {schema}.[XCOUTD] D WITH (UPDLOCK,HOLDLOCK)
                WHERE D.CUNNUM_0=@CountNumber AND D.BOXNUM_0=@BoxNumber;
                """;
            var duplicate = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(duplicateSql,
                    new { CountNumber=countNumber, BoxNumber=label.BoxNumber },
                    transaction, cancellationToken:ct));
            if (duplicate != 0)
                throw new CountWriteException(409, "BOX_ALREADY_RECORDED", "该箱号已扫描！");

            var qty = request.CountedQtyStu.Value;
            // Every mandatory XCOUTD field is assigned explicitly except
            // ROWID, which the preflight above requires SQL Server IDENTITY to generate.
            // Unmapped optional business strings are blank (not SQL NULL).
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
                     CONVERT(binary(16),NEWID()),@AuditUser,@AuditUser);
                """;
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@CountNumber", SqlDbType.NVarChar, 30).Value=countNumber;
            insert.Parameters.Add("@BoxNumber", SqlDbType.NVarChar, 20).Value=label.BoxNumber;
            insert.Parameters.Add("@ItemCode", SqlDbType.NVarChar, 20).Value=label.ItemCode;
            insert.Parameters.Add("@LotNumber", SqlDbType.NVarChar, 30).Value=label.LotNumber;
            insert.Parameters.Add("@Location", SqlDbType.NVarChar, 10).Value=request.CountedLocation;
            insert.Parameters.Add("@StockUnit", SqlDbType.NVarChar, 3).Value=label.StockUnit;
            insert.Parameters.Add("@AuditUser", SqlDbType.NVarChar, 5).Value=options.AuditUser;
            var amount = insert.Parameters.Add("@Quantity", SqlDbType.Decimal);
            amount.Precision=28;
            amount.Scale=13;
            amount.Value=qty;
            var ratio = insert.Parameters.Add("@UnitFactor", SqlDbType.Decimal);
            ratio.Precision=18;
            ratio.Scale=7;
            ratio.Value=1m;
            var detailId = (string?)await insert.ExecuteScalarAsync(ct);
            if (string.IsNullOrWhiteSpace(detailId))
                throw new CountWriteException(503, "ERP_WRITE_FAILED", "XCOUTD 未返回新建 ROWID。");

            const string journalSql = """
                INSERT INTO [dbo].[StockCountSubmission]
                    (Environment,CountNumber,RequestId,PayloadHash,BoxNumber,
                     DetailRowId,StockUnit,QuantityStu,QuantityPcu)
                VALUES
                    (@Environment,@CountNumber,@RequestId,@PayloadHash,@BoxNumber,
                     CONVERT(numeric(38,0),@DetailId),@StockUnit,@Quantity,@Quantity);
                """;
            await connection.ExecuteAsync(new CommandDefinition(journalSql, new {
                Environment=environment.Name, CountNumber=countNumber,
                RequestId=requestId, PayloadHash=payloadHash,
                BoxNumber=label.BoxNumber, DetailId=detailId,
                StockUnit=label.StockUnit, Quantity=qty
            }, transaction, cancellationToken:ct));

            await transaction.CommitAsync(ct);
            return new SaveCountItemResult(environment.Name,countNumber,label.BoxNumber,
                detailId,qty,qty,label.StockUnit,requestId,"Committed",false);
        }
        catch (CountWriteException)
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await SafeRollbackAsync(transaction);
            throw new CountWriteException(409, "IDEMPOTENCY_WRITE_CONFLICT",
                "写入唯一记录发生冲突，请通过原 Idempotency-Key 查询已有提交结果，不要换新请求ID。");
        }
        catch (SqlException error) when (error.Number is 208 or 207 or 229 or 515 or 544 or 8101)
        {
            await SafeRollbackAsync(transaction);
            throw new CountWriteException(503,"ERP_SCHEMA_UNAVAILABLE",
                "SQL Server 结构、权限或 ERP 必填字段不符合写入适配器要求；没有确认保存成功。");
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private static SaveCountItemResult ToResult(SubmissionRow row,bool replayed) =>
        new(row.Environment,row.CountNumber,row.BoxNumber,row.DetailId,
            row.SavedQtyStu,row.SavedQtyPcu,row.StockUnit,row.RequestId,"Committed",replayed);

    private static async Task SafeRollbackAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (InvalidOperationException) { /* The server may have already completed the transaction. */ }
        catch (SqlException) { /* Callers must query the original RequestId after an unknown outcome. */ }
    }

    private sealed class HeaderRow
    {
        public string SiteCode { get; set; } = "";
        public string WarehouseCode { get; set; } = "";
        public int StatusCode { get; set; }
    }

    private sealed class LabelForWrite
    {
        public string BoxNumber { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public string StockUnit { get; set; } = "";
        public string PackageUnit { get; set; } = "";
        public decimal UnitFactor { get; set; }
    }

    private sealed class SubmissionRow
    {
        public string Environment { get; set; } = "";
        public string CountNumber { get; set; } = "";
        public string BoxNumber { get; set; } = "";
        public string DetailId { get; set; } = "";
        public string StockUnit { get; set; } = "";
        public Guid RequestId { get; set; }
        public byte[]? PayloadHash { get; set; }
        public decimal SavedQtyStu { get; set; }
        public decimal SavedQtyPcu { get; set; }
    }
}
