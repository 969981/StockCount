using StockCount.Core;
using StockCount.Infrastructure;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class HistoryPagingTests
{
    private static HistoryQuery Query(string? box = null, string? location = null, string env = "Test")
    {
        Assert.True(HistoryQuery.TryCreate(env, "COUNT-DEMO", box, location, out var query));
        return query;
    }

    [Fact]
    public void FullPrecisionCursorRoundTrip()
    {
        var query = Query("BOX", "LOC");
        var cursor = new HistoryCursor("99999999999999999999999999999999999999",
            "99999999999999999999999999999999999998");
        Assert.True(HistoryCursor.TryDecode(cursor.Encode(query), query, out var result));
        Assert.Equal(cursor, result);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("-1", true)]
    [InlineData("99999999999999999999999999999999999999", true)]
    [InlineData("100000000000000000000000000000000000000", false)]
    [InlineData("01", false)]
    [InlineData("-0", false)]
    [InlineData("1.0", false)]
    [InlineData("1;DROP", false)]
    [InlineData("+1", false)]
    public void Numeric38Validation(string value, bool expected) =>
        Assert.Equal(expected, HistoryCursor.IsRowId(value));

    [Fact]
    public void CursorCannotCrossScopeOrMoveForward()
    {
        var query = Query("BOX", "LOC");
        var token = new HistoryCursor("100", "50").Encode(query);
        Assert.False(HistoryCursor.TryDecode(token, Query("OTHER", "LOC"), out _));
        Assert.False(HistoryCursor.TryDecode(token, Query("BOX", "OTHER"), out _));
        Assert.False(HistoryCursor.TryDecode(token, Query("BOX", "LOC", "Production"), out _));
        Assert.False(HistoryCursor.TryDecode(token, query with { CountNumber = "OTHER" }, out _));
        Assert.False(HistoryCursor.TryDecode(new HistoryCursor("50", "100").Encode(query), query, out _));
        Assert.False(HistoryCursor.TryDecode("50", query, out _));
        Assert.True(HistoryCursor.TryDecode(null, query, out var empty));
        Assert.Null(empty);
    }

    [Fact]
    public void FilterNormalizationAndBounds()
    {
        var query = Query(" BOX ", " LOC ");
        Assert.Equal("BOX", query.BoxSearch);
        Assert.Equal("LOC", query.Location);
        Assert.Null(Query(" ", "").BoxSearch);
        Assert.False(HistoryQuery.TryCreate("Test", "COUNT", new string('x',21), null, out _));
        Assert.False(HistoryQuery.TryCreate("Test", "COUNT", null, new string('x',11), out _));
        Assert.False(HistoryQuery.TryCreate("Test", "COUNT", "x\ny", null, out _));
    }

    [Theory]
    [InlineData(0,false)]
    [InlineData(1,true)]
    [InlineData(50,true)]
    [InlineData(100,true)]
    [InlineData(101,false)]
    public void PageSizeBounds(int size, bool valid) => Assert.Equal(valid, HistoryPagingPolicy.ValidSize(size));

    [Fact]
    public void BoundedAutoPaging()
    {
        Assert.True(HistoryPagingPolicy.CanAutoLoad(450,true,false,false));
        Assert.False(HistoryPagingPolicy.CanAutoLoad(500,true,false,false));
        Assert.False(HistoryPagingPolicy.CanAutoLoad(50,false,false,false));
        Assert.False(HistoryPagingPolicy.CanAutoLoad(50,true,true,false));
        Assert.False(HistoryPagingPolicy.CanAutoLoad(50,true,false,true));
    }

    [Fact]
    public void SqlUsesNativeNumericKeysetAndParameterizedFilters()
    {
        var env = new SelectedEnvironment("Test", "XTEST");
        var first = HistoryReadQuery.Page(env,false);
        var next = HistoryReadQuery.Page(env,true);
        Assert.Contains("[XTEST].[XCOUTD]",first);
        Assert.Contains("TOP (@Take)",first);
        Assert.Contains("D.ROWID<=CONVERT(numeric(38,0),@UpperRowId)",first);
        Assert.DoesNotContain("@BeforeRowId",first);
        Assert.Contains("D.ROWID<CONVERT(numeric(38,0),@BeforeRowId)",next);
        Assert.Contains("CHARINDEX(@BoxSearch,D.BOXNUM_0)",next);
        Assert.Contains("D.LOC_0=@Location",next);
        Assert.Contains("D.CUNNUM_0=@CountNumber",next);
        Assert.DoesNotContain("OFFSET",next);
        Assert.Contains("COUNT_BIG(*)",HistoryReadQuery.Summary(env));
        Assert.Contains("[COB].[XCOUTD]",HistoryReadQuery.Page(new("Production","COB"),true));
        Assert.Throws<InvalidOperationException>(() => HistoryReadQuery.Page(new("Test","OTHER"),false));
    }
}
