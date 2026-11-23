using Microsoft.Data.Sqlite;
using System.Globalization;

namespace StockCount.Server.Hosting;

public sealed record ServerLog(long Id, string TimestampUtc, string Level, string Category,
    string Message, string? Exception);

public sealed class LogReader(SettingsStore store)
{
    /// <summary>
    /// Delete historical rows from the existing SQLite DB. A second connection is safe
    /// with WAL mode; never delete the database, WAL, or SHM files while API is running.
    /// Invalid ranges are rejected before opening a connection.
    /// </summary>
    public int DeleteOlderThan(int days, DateTimeOffset? referenceUtc = null)
    {
        if (days is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(days));
        if (!File.Exists(store.LogPath)) return 0;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = store.LogPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Default
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=5000";
            busy.ExecuteNonQuery();
        }

        using var tx = connection.BeginTransaction();
        using var delete = connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM log_entries WHERE timestamp_utc < $cutoff";
        delete.Parameters.AddWithValue("$cutoff",
            (referenceUtc ?? DateTimeOffset.UtcNow).ToUniversalTime()
            .AddDays(-days).ToString("O", CultureInfo.InvariantCulture));
        var removed = delete.ExecuteNonQuery();
        tx.Commit();
        return removed;
    }

    public IReadOnlyList<ServerLog> Read(string? search = null, int limit = 200)
    {
        if (!File.Exists(store.LogPath)) return Array.Empty<ServerLog>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = store.LogPath, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Default
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=2000";
            busy.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,timestamp_utc,level,category,message,exception
            FROM log_entries
            WHERE $pattern IS NULL
               OR category LIKE $pattern OR level LIKE $pattern OR message LIKE $pattern
            ORDER BY id DESC LIMIT $limit
            """;
        var trimmed = search?.Trim();
        command.Parameters.AddWithValue("$pattern",
            string.IsNullOrEmpty(trimmed) ? DBNull.Value : "%" + trimmed[..Math.Min(trimmed.Length, 80)] + "%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var rows = new List<ServerLog>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new ServerLog(reader.GetInt64(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }
}
