using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StockCount.Api;
using StockCount.Contracts;
using StockCount.Core;
using StockCount.Infrastructure;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class HistoryEndpointTests
{
    [Fact]
    public async Task NewEndpointValidatesScopeAndFilters()
    {
        var fake = new HistoryRepository();
        await using var app = ApiApplication.Build(Array.Empty<string>(), builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["StockCount:DeviceKey"] = "";
            builder.Services.AddSingleton<IInventoryHistoryRepository>(fake);
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            client.DefaultRequestHeaders.Add("X-StockCount-Environment", "Test");
            const string path = "/api/v1/inventory-counts/COUNT-DEMO/history";
            var page = await client.GetFromJsonAsync<CountHistoryPage>(path + "?boxSearch=BOX&location=LOC&pageSize=50");
            Assert.NotNull(page);
            Assert.Equal(101L, page.TotalCount);
            Assert.Equal(51L, page.FilteredCount);
            Assert.Equal("BOX", fake.LastQuery!.BoxSearch);
            Assert.Equal("LOC", fake.LastQuery.Location);
            Assert.Equal(50, fake.LastSize);
            Assert.Null(fake.LastCursor);
            Assert.NotNull(page.NextCursor);
            var next = await client.GetFromJsonAsync<CountHistoryPage>(path + "?boxSearch=BOX&location=LOC&cursor=" +
                Uri.EscapeDataString(page.NextCursor));
            Assert.NotNull(next);
            Assert.Null(next.TotalCount);
            Assert.Null(next.FilteredCount);
            Assert.Equal("100", fake.LastCursor!.BeforeRowId);
            var calls = fake.Calls;
            foreach (var invalid in new[] { "?pageSize=101", "?location=12345678901",
                "?boxSearch=123456789012345678901", "?cursor=50",
                "?boxSearch=OTHER&location=LOC&cursor=" + Uri.EscapeDataString(page.NextCursor) })
            {
                using var response = await client.GetAsync(path + invalid);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            Assert.Equal(calls, fake.Calls);
            using var missing = await client.GetAsync("/api/v1/inventory-counts/MISSING/history");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class HistoryRepository : IInventoryHistoryRepository
    {
        public int Calls { get; private set; }
        public int LastSize { get; private set; }
        public HistoryQuery? LastQuery { get; private set; }
        public HistoryCursor? LastCursor { get; private set; }

        public Task<CountHistoryPage?> GetHistoryAsync(SelectedEnvironment environment,
            HistoryQuery query, HistoryCursor? cursor, int size, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            LastCursor = cursor;
            LastSize = size;
            if (query.CountNumber == "MISSING") return Task.FromResult<CountHistoryPage?>(null);
            return Task.FromResult<CountHistoryPage?>(new CountHistoryPage(environment.Name,
                Array.Empty<CountItemDto>(), cursor is null ? new HistoryCursor("150", "100").Encode(query) : null,
                cursor is null ? 101L : null, cursor is null ? 51L : null));
        }
    }
}
