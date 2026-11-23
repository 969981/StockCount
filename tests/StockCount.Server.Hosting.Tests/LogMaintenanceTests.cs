using Microsoft.Data.Sqlite;
using StockCount.Server.Hosting;
using Xunit;

namespace StockCount.Server.Hosting.Tests;

public sealed class LogMaintenanceTests
{
    [Fact]
    public void LocalizesHostingMessagesWithoutChangingOriginal()
    {
        var input = new ServerLog(1, "2026-11-23T08:00:00.0000000+00:00",
            "Information", "Microsoft.Hosting.Lifetime",
            "Now listening on: http://localhost:8898", null);
        var translated = LogLocalizer.ToChinese(input);
        Assert.Equal("信息", translated.Level);
        Assert.Equal("服务生命周期", translated.Category);
        Assert.Equal("正在监听：http://localhost:8898", translated.Message);
        Assert.Equal("Now listening on: http://localhost:8898", input.Message);
        Assert.NotEqual(input.TimestampUtc, translated.TimestampUtc);
    }

    [Fact]
    public void RetainsUnknownExceptionText()
    {
        const string raw = "SqlException: Login failed for user (diagnostic details).";
        Assert.Equal(raw, LogLocalizer.LocalizeMessage(raw));
        Assert.Contains("状态码 401", LogLocalizer.LocalizeMessage("HTTP GET /api/v1/inventory-counts -> 401 (5 ms)"));
        Assert.Contains("开发环境", LogLocalizer.LocalizeMessage("Hosting environment: Development"));
    }

    [Fact]
    public void PurgesOnlyOldRowsAndLeavesNewRowsInWalDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "stockcount-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(root);
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = store.LogPath, Mode = SqliteOpenMode.ReadWriteCreate }.ConnectionString))
            {
                conn.Open();
                using var schema = conn.CreateCommand();
                schema.CommandText = """
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE log_entries(
                        id INTEGER PRIMARY KEY, timestamp_utc TEXT NOT NULL, level TEXT NOT NULL,
                        category TEXT NOT NULL, message TEXT NOT NULL, exception TEXT
                    );
                    INSERT INTO log_entries(timestamp_utc,level,category,message)
                    VALUES ('2026-10-01T00:00:00.0000000+00:00','Information','Test','old'),
                           ('2026-11-22T00:00:00.0000000+00:00','Information','Test','new');
                    """;
                schema.ExecuteNonQuery();
            }
            var reader = new LogReader(store);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.DeleteOlderThan(0));
            Assert.Equal(1, reader.DeleteOlderThan(7,
                new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero)));
            Assert.Single(reader.Read());
            Assert.Equal("new", reader.Read()[0].Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingLogDatabaseDoesNotGetCreatedByCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "stockcount-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(root);
            Assert.Equal(0, new LogReader(store).DeleteOlderThan(30));
            Assert.False(File.Exists(store.LogPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
