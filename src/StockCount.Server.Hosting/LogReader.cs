using Microsoft.Data.Sqlite;

namespace StockCount.Server.Hosting;

public sealed record ServerLog(long Id, string TimestampUtc, string Level, string Category,
    string Message, string? Exception);

public sealed class LogReader(SettingsStore store)
{
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
