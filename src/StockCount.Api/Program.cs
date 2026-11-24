using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using StockCount.Api;
using StockCount.Api.Logging;

static string Env(string name) => Environment.GetEnvironmentVariable(name)?.Trim() ?? "";
static bool Enabled(string name) => string.Equals(Env(name), "true", StringComparison.OrdinalIgnoreCase);

var sqlServer = Env("STOCKCOUNT_SQL_SERVER");
var database = Env("STOCKCOUNT_SQL_DATABASE");
var connection = "";
if (!string.IsNullOrWhiteSpace(sqlServer))
{
    var sql = new SqlConnectionStringBuilder
    {
        DataSource = sqlServer,
        InitialCatalog = string.IsNullOrWhiteSpace(database) ? "sagex3" : database,
        IntegratedSecurity = Enabled("STOCKCOUNT_SQL_INTEGRATED"),
        ConnectTimeout = 5,
        TrustServerCertificate = Enabled("STOCKCOUNT_SQL_TRUST_CERT")
    };
    sql["Encrypt"] = Enabled("STOCKCOUNT_SQL_NO_ENCRYPT") ? "False" : "True";
    if (!sql.IntegratedSecurity)
    {
        sql.UserID = Env("STOCKCOUNT_SQL_USER");
        sql.Password = Environment.GetEnvironmentVariable("STOCKCOUNT_SQL_PASSWORD") ?? "";
    }
    connection = sql.ConnectionString;
}

// Run a one-shot database connectivity test without hosting HTTP.
// Used by the desktop manager's “测试连接” button even while the API is stopped.
if (args.Contains("--test-db", StringComparer.Ordinal))
{
    try
    {
        if (connection.Length == 0)
            throw new InvalidOperationException("请先填写 SQL Server 地址。");
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = new SqlCommand("SELECT 1", sql);
        var result = await command.ExecuteScalarAsync();
        if (Convert.ToInt32(result) != 1)
            throw new InvalidOperationException("数据库连接测试结果异常。");
        Console.WriteLine("数据库连接成功。");
        return;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("数据库连接失败：" + error.GetType().Name);
        Environment.ExitCode = 2;
        return;
    }
}

// In packaged Tauri mode, Rust supplies the actual GUI exe directory.
// Standalone CLI mode defaults to the API exe directory.
// No implicit fallback to AppData: the operator explicitly requested a colocated database.
var logDb = Environment.GetEnvironmentVariable("STOCKCOUNT_LOG_DB_PATH");
if (string.IsNullOrWhiteSpace(logDb))
    logDb = Path.Combine(AppContext.BaseDirectory, "server-logs.db");
if (!Path.IsPathFullyQualified(logDb))
    throw new InvalidOperationException("SQLite 日志路径必须是绝对路径。");
await using var sqlite = new SqliteLogStore(logDb);

var portText = Env("STOCKCOUNT_API_PORT");
var hasHostPort = int.TryParse(portText, out var port);
if (hasHostPort && (port is < 1024 or >= 65535))
    throw new InvalidOperationException("API 端口必须在 1024..65534，额外占用本机端口+1 做服务管理。");

var allowLan = Enabled("STOCKCOUNT_ALLOW_LAN");
// LAN HTTP is for isolated trusted PDA networks. Keyless access is a separate
// explicit host setting; remote public peers are still rejected.
var allowKeylessLan = hasHostPort && allowLan && Enabled("STOCKCOUNT_ALLOW_KEYLESS_LAN");
if (hasHostPort && allowLan && !allowKeylessLan && string.IsNullOrEmpty(Env("STOCKCOUNT_DEVICE_KEY")))
    throw new InvalidOperationException("开启局域网 HTTP 之前必须配置设备访问密钥。");

var app = ApiApplication.Build(args, builder =>
{
    // Both standalone API and the Tauri Sidecar honor the two independent
    // server-side write approval switches. Defaults fail closed.
    var configuration = new Dictionary<string, string?>
    {
        ["StockCount:EnableTestWrites"] = Enabled("STOCKCOUNT_ENABLE_TEST_WRITES").ToString(),
        ["StockCount:EnableProductionWrites"] = Enabled("STOCKCOUNT_ENABLE_PRODUCTION_WRITES").ToString(),
        ["StockCount:Write:TestSchemaReviewed"] = Enabled("STOCKCOUNT_XTEST_SCHEMA_REVIEWED").ToString(),
        ["StockCount:Write:ProductionSchemaReviewed"] = Enabled("STOCKCOUNT_COB_SCHEMA_REVIEWED").ToString(),
        ["StockCount:Write:AuditUser"] = Env("STOCKCOUNT_X3_AUDIT_USER")
    };
    if (!string.IsNullOrWhiteSpace(connection))
        configuration["ConnectionStrings:SageX3"] = connection;

    if (hasHostPort)
    {
        configuration["StockCount:DeviceKey"] = allowKeylessLan ? "" : Env("STOCKCOUNT_DEVICE_KEY");
        configuration["StockCount:AllowKeylessLan"] = allowKeylessLan.ToString();
        configuration["StockCount:AdminToken"] = Env("STOCKCOUNT_ADMIN_TOKEN");
        configuration["AllowedHosts"] = allowLan ? "*" : "localhost;127.0.0.1";
        builder.WebHost.ConfigureKestrel(options =>
        {
            // The management port stays loopback only. LAN access is an explicit
            // opt-in and listens over HTTP; restrict port access via LAN firewall.
            options.ListenLocalhost(port + 1);
            if (allowLan)
                options.ListenAnyIP(port);
            else
                options.ListenLocalhost(port);
        });
    }
    builder.Configuration.AddInMemoryCollection(configuration);
    builder.Logging.AddProvider(sqlite);
});

try { await app.RunAsync(); }
finally { await app.DisposeAsync(); }
