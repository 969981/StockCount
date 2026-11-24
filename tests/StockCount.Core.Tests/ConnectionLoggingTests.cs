using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockCount.Api;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ConnectionLoggingTests
{
    [Fact]
    public async Task MarkedHealthAndRejectedReadinessAreLoggedWithoutSecrets()
    {
        using var capture = new CaptureProvider();
        await using var app = ApiApplication.Build(Array.Empty<string>(), builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StockCount:DeviceKey"] = "fixture-server-key"
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(capture);
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            // The desktop's unmarked loopback health poll should not create a PDA diagnostic.
            using (var poll = await client.GetAsync("/health"))
                Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
            Assert.Empty(capture.Entries);

            var id = Guid.NewGuid().ToString("D");
            using var health = new HttpRequestMessage(HttpMethod.Get, "/health");
            Mark(health, id);
            using var healthResult = await client.SendAsync(health);
            Assert.Equal(HttpStatusCode.OK, healthResult.StatusCode);

            using var ready = new HttpRequestMessage(HttpMethod.Get, "/ready");
            Mark(ready, id);
            ready.Headers.Add("X-StockCount-Device-Key", "fixture-wrong-key");
            using var readyResult = await client.SendAsync(ready);
            Assert.Equal(HttpStatusCode.Unauthorized, readyResult.StatusCode);

            // StopAsync drains in-flight requests before reading the final middleware logs.
            await app.StopAsync();
            var logs = capture.Entries.ToArray();
            Assert.Equal(4, logs.Length);
            Assert.All(logs, line =>
            {
                Assert.Contains(id, line);
                Assert.Contains("127.0.0.1", line);
                Assert.DoesNotContain("fixture-server-key", line);
                Assert.DoesNotContain("fixture-wrong-key", line);
            });
            Assert.Contains(logs, line => line.Contains("PDA 连接请求") && line.Contains("API 健康"));
            Assert.Contains(logs, line => line.Contains("API 可达") && line.Contains("HTTP 200"));
            Assert.Contains(logs, line => line.Contains("数据库就绪") && line.Contains("XTEST"));
            Assert.Contains(logs, line => line.Contains("设备密钥未授权") && line.Contains("HTTP 401"));
        }
        finally { await app.StopAsync(); }
    }

    private static void Mark(HttpRequestMessage request, string id)
    {
        request.Headers.Add("X-StockCount-Connection-Test", "1");
        request.Headers.Add("X-StockCount-Diagnostic-Id", id);
        request.Headers.Add("X-StockCount-Environment", "Test");
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }

        private sealed class CaptureLogger(CaptureProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) =>
                category == "StockCount.Api.Connection" && level >= LogLevel.Information;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(level)) provider.Entries.Enqueue(formatter(state, error));
            }
        }
    }
}
