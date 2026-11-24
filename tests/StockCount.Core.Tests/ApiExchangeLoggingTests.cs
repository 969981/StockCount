using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockCount.Api;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ApiExchangeLoggingTests
{
    [Fact]
    public async Task CapturesRequestAndResponseWithoutChangingPayloadOrLoggingCredentials()
    {
        using var capture = new CaptureProvider();
        await using var app = ApiApplication.Build(Array.Empty<string>(), builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StockCount:DeviceKey"] = "fixture-key"
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(capture);
        });
        app.MapPost("/api/v1/log-fixture/{countNumber}", (string countNumber, JsonElement body) =>
            Results.Json(new { countNumber, locationCode = body.GetProperty("rawCode").GetString(),
                isValid = true, password = "response-secret", unknown = "response-private" }));
        app.MapGet("/api/v1/large-fixture", () => Results.Json(new { items = new string('x', 12000) }));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            client.DefaultRequestHeaders.Add("X-StockCount-Device-Key", "fixture-key");
            client.DefaultRequestHeaders.Add("X-StockCount-Environment", "Test");
            using (var poll = await client.GetAsync("/health"))
                Assert.Equal(HttpStatusCode.OK, poll.StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/api/v1/log-fixture/COUNT-DEMO?search=fixture-search&token=query-secret");
            request.Headers.Add("Idempotency-Key", "11111111-1111-4111-8111-111111111111");
            request.Content = JsonContent.Create(new
            {
                rawCode = "LC.DEMO-01",
                password = "request-secret",
                unknown = "request-private"
            });
            using var result = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            Assert.Equal("LC.DEMO-01", json.RootElement.GetProperty("locationCode").GetString());
            Assert.Equal("response-secret", json.RootElement.GetProperty("password").GetString());

            using var large = await client.GetAsync("/api/v1/large-fixture");
            Assert.Equal(HttpStatusCode.OK, large.StatusCode);
            Assert.Contains(new string('x', 12000), await large.Content.ReadAsStringAsync());

            using var rejected = new HttpRequestMessage(HttpMethod.Get, "/ready");
            rejected.Headers.Add("X-StockCount-Device-Key", "wrong-key");
            using var rejection = await client.SendAsync(rejected);
            Assert.Equal(HttpStatusCode.Unauthorized, rejection.StatusCode);

            await app.StopAsync();
            var logs = capture.Entries.ToArray();
            Assert.Equal(3, logs.Length);
            var entry = Assert.Single(logs.Where(x => x.Contains("log-fixture")));
            foreach (var value in new[] { "127.0.0.1", "XTEST", "HTTP 200", "COUNT-DEMO",
                "fixture-search", "LC.DEMO-01", "入参 JSON", "出参 JSON", "isValid",
                "11111111-1111-4111-8111-111111111111", "请求编号" })
                Assert.Contains(value, entry);
            Assert.Contains(logs, x => x.Contains("正文超过") && x.Contains("large-fixture"));
            Assert.Contains(logs, x => x.Contains("HTTP 401") && x.Contains("DEVICE_NOT_AUTHORIZED"));
            foreach (var row in logs)
                foreach (var secret in new[] { "fixture-key", "wrong-key", "query-secret",
                    "request-secret", "response-secret", "request-private", "response-private" })
                    Assert.DoesNotContain(secret, row);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string category) => new CaptureLogger(this, category);
        public void Dispose() { }
        private sealed class CaptureLogger(CaptureProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) =>
                category == "StockCount.Api.Logging.ApiExchangeLogging";
            public void Log<TState>(LogLevel level, EventId eventId, TState state,
                Exception? error, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(level)) provider.Entries.Enqueue(formatter(state, error));
            }
        }
    }
}
