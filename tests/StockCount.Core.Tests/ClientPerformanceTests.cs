using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class ClientPerformanceTests
{
    [Fact]
    public void BoundedSamplesAndNearestRankPercentiles()
    {
        var perf = new ClientPerformance(4);
        foreach (var ms in new[] { 100d, 10d, 20d, 30d, 40d })
            perf.Record(ClientPerfStage.ScanTotal, ms);
        var s = Assert.Single(perf.Snapshot());
        Assert.Equal(4, s.Count);
        Assert.Equal(40d, s.LatestMs);
        Assert.Equal(20d, s.P50Ms);
        Assert.Equal(40d, s.P95Ms);
        perf.Clear();
        Assert.Empty(perf.Snapshot());
    }

    [Fact]
    public void DifferentStagesRemainSeparateAndInvalidSamplesIgnored()
    {
        var perf = new ClientPerformance();
        perf.Record(ClientPerfStage.HttpResponse, 10);
        perf.Record(ClientPerfStage.DraftRead, 20);
        perf.Record(ClientPerfStage.HttpResponse, double.NaN);
        perf.Record(ClientPerfStage.HttpResponse, -1);
        Assert.Equal(2, perf.Snapshot().Length);
        Assert.All(perf.Snapshot(), s => Assert.Equal(1, s.Count));
    }
}
