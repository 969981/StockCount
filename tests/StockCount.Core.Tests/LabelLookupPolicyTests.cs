using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class LabelLookupPolicyTests
{
    private const string Qr = "ID.1234567890123|1@@WO.DEMO01261000001@@LN.1000@@MT.P00000001@@QT.100@@LT.L260101";
    private const string BoxId = "1234567890123|1";

    [Theory]
    [InlineData(Qr, BoxId)]
    [InlineData(Qr + "\u4E8C", BoxId)]
    [InlineData("WO.DEMO@@ID.BOX-01@@LT.LOT", "BOX-01")]
    [InlineData("ID.A@@ID.B@@LT.LOT", null)]
    [InlineData("ID.@@LT.LOT", null)]
    [InlineData("ID.123456789012345678901@@LT.LOT", null)]
    [InlineData("ID.A B@@LT.LOT", null)]
    [InlineData("LC.410300000", null)]
    [InlineData("ID.BOX-01", null)]
    public void ExtractsOneCompleteStructuredId(string qr, string? expected)
        => Assert.Equal(expected, LabelLookupPolicy.ExtractBoxId(qr));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExactMatchesAlwaysTakePriority(int count)
    {
        var exact = Enumerable.Repeat("exact", count).ToArray();
        var matches = await LabelLookupPolicy.FindAsync(Qr,
            raw => { Assert.Equal(Qr, raw); return Task.FromResult(exact); },
            _ => throw new InvalidOperationException("Unexpected fallback"));
        Assert.Same(exact, matches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExactMissReturnsBoxMatchesWithoutPickingAmbiguousRows(int count)
    {
        var raw = Qr + "\u4E8C";
        var fallback = Enumerable.Repeat("label", count).ToArray();
        var matches = await LabelLookupPolicy.FindAsync(raw,
            value => { Assert.Equal(raw, value); return Task.FromResult(Array.Empty<string>()); },
            id => { Assert.Equal(BoxId, id); return Task.FromResult(fallback); });
        Assert.Same(fallback, matches);
    }

    [Fact]
    public async Task InvalidIdDoesNotTriggerFallback()
    {
        var result = await LabelLookupPolicy.FindAsync("ID.A@@ID.B",
            _ => Task.FromResult(Array.Empty<string>()),
            _ => throw new InvalidOperationException("Unexpected fallback"));
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryExceptionDoesNotTriggerFallback()
    {
        await Assert.ThrowsAsync<IOException>(() => LabelLookupPolicy.FindAsync(Qr,
            _ => Task.FromException<string[]>(new IOException("database unavailable")),
            _ => throw new InvalidOperationException("Unexpected fallback")));
    }
}
