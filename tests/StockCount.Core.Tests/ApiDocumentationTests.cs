using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using StockCount.Api;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ApiDocumentationTests
{
    [Fact]
    public async Task ServesChineseSwaggerAndElevenOperationsWithoutDatabase()
    {
        await using var app = ApiApplication.Build(Array.Empty<string>(),
            builder => builder.WebHost.UseUrls("http://127.0.0.1:0"));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var route in new[] { "/", "/swagger", "/swagger/index.html" })
            {
                using var redirect = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
                Assert.Equal("/docs/index.html", redirect.Headers.Location?.OriginalString);
            }
            var html = await client.GetStringAsync("/docs/index.html");
            Assert.Contains("swagger-ui-bundle.js", html);
            Assert.Contains("/docs/zh-cn.js", html);
            var configScript = await client.GetStringAsync("/docs/index.js");
            Assert.Contains("/openapi/v1.json", configScript);
            Assert.Contains("StockCount", html);
            Assert.Contains("supportedSubmitMethods", configScript);
            var script = await client.GetStringAsync("/docs/zh-cn.js");
            Assert.Contains("在线测试", script);
            using var asset = await client.GetAsync("/docs/swagger-ui-bundle.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);

            using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
            var root = doc.RootElement;
            Assert.Contains("库存盘点", root.GetProperty("info").GetProperty("title").GetString());
            var paths = root.GetProperty("paths");
            Assert.Equal(11, paths.EnumerateObject().Sum(p => p.Value.EnumerateObject().Count()));
            Assert.False(paths.TryGetProperty("/internal/shutdown", out _));
            Assert.False(paths.TryGetProperty("/docs/zh-cn.js", out _));
            var counts = paths.GetProperty("/api/v1/inventory-counts").GetProperty("get");
            Assert.Equal("查询盘点单列表", counts.GetProperty("summary").GetString());
            Assert.Contains(counts.GetProperty("parameters").EnumerateArray(),
                p => p.GetProperty("name").GetString() == "X-StockCount-Environment" &&
                     p.GetProperty("required").GetBoolean());
            var history = paths.GetProperty("/api/v1/inventory-counts/{countNumber}/history").GetProperty("get");
            Assert.Equal("查询已盘记录", history.GetProperty("summary").GetString());
            Assert.Contains(history.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "boxSearch");
            Assert.Contains(history.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "location");
            var submit = paths.GetProperty("/api/v1/inventory-counts/{countNumber}/items").GetProperty("post");
            Assert.Contains("实际写入", submit.GetProperty("summary").GetString());
            Assert.Contains(submit.GetProperty("parameters").EnumerateArray(),
                p => p.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.True(submit.GetProperty("responses").TryGetProperty("201", out _));
            Assert.True(submit.GetProperty("responses").TryGetProperty("200", out _));
            var update = paths.GetProperty("/api/v1/inventory-counts/{countNumber}/items/{detailId}").GetProperty("patch");
            Assert.Equal("更新同单同库位原明细数量", update.GetProperty("summary").GetString());
            Assert.Contains(update.GetProperty("parameters").EnumerateArray(),
                p => p.GetProperty("name").GetString() == "Idempotency-Key" &&
                     p.GetProperty("required").GetBoolean());
            Assert.True(update.GetProperty("responses").TryGetProperty("200", out _));
            Assert.False(update.GetProperty("responses").TryGetProperty("201", out _));
            var schemas = root.GetProperty("components").GetProperty("schemas");
            Assert.True(schemas.GetProperty("UpdateCountItemRequest").GetProperty("properties").TryGetProperty("expectedVersion", out _));
            Assert.Equal("人工录入实盘数量，必填", schemas.GetProperty("SaveCountItemRequest")
                .GetProperty("properties").GetProperty("countedQtyStu").GetProperty("description").GetString());
            using var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
