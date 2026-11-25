using System.Text.Json.Serialization;

namespace StockCount.Server.Hosting;

// JSON property names deliberately match the existing Tauri/Rust serde camelCase file.
public sealed class ServerSettings
{
    // Existing Tauri JSON files without this property default to auto-starting the API
    // after a valid SQL server address has been configured.
    public bool AutoStartApi { get; set; } = true;
    public int ApiPort { get; set; } = 5080;
    public bool AllowLan { get; set; }
    // Existing JSON without this opt-in retains the old keyed policy.
    public bool AllowKeylessLan { get; set; }
    public string CertPath { get; set; } = "";
    public const string DefaultSqlServer = "192.168.1.1,1433";
    public const string DefaultAuditUser = "PDA";
    public string SqlServer { get; set; } = DefaultSqlServer;
    public string SqlDatabase { get; set; } = "sagex3";
    public bool IntegratedSecurity { get; set; }
    public string SqlUser { get; set; } = "";
    public bool EncryptSql { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public bool TestCheckSite { get; set; }
    public bool TestCheckWarehouse { get; set; }
    public bool ProductionCheckSite { get; set; }
    public bool ProductionCheckWarehouse { get; set; }
    public bool EnableTestWrites { get; set; }
    public bool EnableProductionWrites { get; set; }
    public bool SchemaReviewed { get; set; }
    public bool ProductionSchemaReviewed { get; set; }
    public string X3AuditUser { get; set; } = "";

    // An application identifier, not a claimed individual X3 operator.
    [JsonIgnore]
    public string EffectiveX3AuditUser => string.IsNullOrWhiteSpace(X3AuditUser)
        ? DefaultAuditUser : X3AuditUser.Trim();

    public ServerSettings Copy() => (ServerSettings)MemberwiseClone();

    public void CopyWriteApprovalsFrom(ServerSettings source)
    {
        SchemaReviewed = source.SchemaReviewed;
        EnableTestWrites = source.EnableTestWrites;
        ProductionSchemaReviewed = source.ProductionSchemaReviewed;
        EnableProductionWrites = source.EnableProductionWrites;
        X3AuditUser = source.X3AuditUser;
    }

    // A UI preset, not a saved or effective authorization.
    public ServerSettings CreateWriteDraft()
    {
        var draft = Copy();
        if (string.IsNullOrWhiteSpace(X3AuditUser) && !SchemaReviewed && !EnableTestWrites &&
            !ProductionSchemaReviewed && !EnableProductionWrites)
        {
            draft.SchemaReviewed = draft.EnableTestWrites = true;
            draft.ProductionSchemaReviewed = draft.EnableProductionWrites = true;
        }
        return draft;
    }

    public void Validate()
    {
        if (ApiPort is < 1024 or > 65534)
            throw new InvalidOperationException("API 端口必须为 1024–65534，端口+1 用于仅本机管理。");
        if (string.IsNullOrWhiteSpace(SqlDatabase))
            throw new InvalidOperationException("数据库名不能为空。");
        if (EnableTestWrites && !SchemaReviewed)
            throw new InvalidOperationException("XTEST 未完成独立核验，不允许开启写入。");
        if (EnableProductionWrites && !ProductionSchemaReviewed)
            throw new InvalidOperationException("COB 未完成独立核验，不允许开启写入。");
        if ((EnableTestWrites || EnableProductionWrites) &&
            (EffectiveX3AuditUser.Length > 5 || EffectiveX3AuditUser.Any(char.IsControl)))
            throw new InvalidOperationException("自定义 X3 操作员编码须为 1–5 字符；留空使用 PDA。");
    }

    public bool DatabaseIdentityChangedFrom(ServerSettings previous, bool newPassword) =>
        newPassword ||
        !string.Equals(SqlServer, previous.SqlServer, StringComparison.Ordinal) ||
        !string.Equals(SqlDatabase, previous.SqlDatabase, StringComparison.Ordinal) ||
        IntegratedSecurity != previous.IntegratedSecurity ||
        !string.Equals(SqlUser, previous.SqlUser, StringComparison.Ordinal) ||
        EncryptSql != previous.EncryptSql ||
        TrustServerCertificate != previous.TrustServerCertificate;

    public void RevokeWriteApprovals()
    {
        EnableTestWrites = false;
        EnableProductionWrites = false;
        SchemaReviewed = false;
        ProductionSchemaReviewed = false;
    }
}
