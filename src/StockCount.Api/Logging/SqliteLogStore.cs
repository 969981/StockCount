using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace StockCount.Api.Logging;

public sealed record LogRow(long Id, string TimestampUtc, string Level, string Category,
    string Message, string? Exception);

/// <summary>
/// Per-user SQLite log database. The API and desktop UI send log records to one writer.
/// Bounded queue prevents unbounded memory use under noisy requests.
/// </summary>
public sealed class SqliteLogStore : ILoggerProvider, IAsyncDisposable
{
    private readonly string _path;
    private readonly Channel<LogRow> _channel;
    private readonly Task _worker;
    private long _dropped;
    public Exception? LastWriteError { get; private set; }
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public SqliteLogStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var connection = Open())
        {
            connection.Open();
            using var initialize = connection.CreateCommand();
            initialize.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS log_entries(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp_utc TEXT NOT NULL,
                    level TEXT NOT NULL,
                    category TEXT NOT NULL,
                    message TEXT NOT NULL,
                    exception TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_log_entries_time
                    ON log_entries(timestamp_utc DESC);
                """;
            initialize.ExecuteNonQuery();
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM log_entries WHERE timestamp_utc < $cutoff;";
            cleanup.Parameters.AddWithValue("$cutoff",
                DateTimeOffset.UtcNow.AddDays(-30).ToString("O", CultureInfo.InvariantCulture));
            cleanup.ExecuteNonQuery();
        }

        _channel = Channel.CreateBounded<LogRow>(new BoundedChannelOptions(5000)
        {
            SingleWriter = false, SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        _worker = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new SqliteLogger(this, categoryName);

    public void Add(string level, string category, string message, Exception? error = null)
    {
        var record = new LogRow(0, DateTimeOffset.UtcNow.ToString("O"),
            level, Clean(category), Clean(message), error is null ? null : Clean(error.ToString()));
        if (!_channel.Writer.TryWrite(record))
            Interlocked.Increment(ref _dropped);
    }

    public IReadOnlyList<LogRow> Latest(int take = 250, string? filter = null)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,timestamp_utc,level,category,message,exception
            FROM log_entries
            WHERE $filter IS NULL OR category LIKE '%' || $filter || '%'
              OR level LIKE '%' || $filter || '%'
              OR message LIKE '%' || $filter || '%'
            ORDER BY id DESC LIMIT $take;
            """;
        command.Parameters.AddWithValue("$filter", string.IsNullOrWhiteSpace(filter) ?
            DBNull.Value : filter.Trim());
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 1000));
        using var reader = command.ExecuteReader();
        var rows = new List<LogRow>();
        while (reader.Read())
        {
            rows.Add(new LogRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows;
    }

    private SqliteConnection Open() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString());

    private async Task WriteLoopAsync()
    {
        try
        {
            await using var connection = Open();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO log_entries(timestamp_utc,level,category,message,exception)
                VALUES($timestamp,$level,$category,$message,$exception);
                """;
            var timestamp = command.Parameters.Add("$timestamp", SqliteType.Text);
            var level = command.Parameters.Add("$level", SqliteType.Text);
            var category = command.Parameters.Add("$category", SqliteType.Text);
            var message = command.Parameters.Add("$message", SqliteType.Text);
            var exception = command.Parameters.Add("$exception", SqliteType.Text);
            await foreach (var row in _channel.Reader.ReadAllAsync())
            {
                timestamp.Value = row.TimestampUtc;
                level.Value = row.Level;
                category.Value = row.Category;
                message.Value = row.Message;
                exception.Value = row.Exception is null ? DBNull.Value : row.Exception;
                try { await command.ExecuteNonQueryAsync(); }
                catch (Exception error) { LastWriteError = error; }
            }
        }
        catch (Exception error)
        {
            LastWriteError = error;
        }
    }

    // Simple defense in depth: logs must never echo obvious credential assignments.
    private static string Clean(string value)
    {
        var limited = value.Length > 4000 ? value[..4000] : value;
        return Regex.Replace(limited,
            @"(?i)\b(password|pwd|device-key|access-key|apikey|api_key)\s*[:=]\s*[^;\s,]+",
            "$1=[REDACTED]");
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _worker;
        GC.SuppressFinalize(this);
    }

    public void Dispose() => _channel.Writer.TryComplete();

    private sealed class SqliteLogger(SqliteLogStore store, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information
            && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            store.Add(logLevel.ToString(), categoryName, formatter(state, exception), exception);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
