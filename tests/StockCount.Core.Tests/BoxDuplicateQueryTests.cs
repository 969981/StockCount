using StockCount.Core;
using StockCount.Infrastructure;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class BoxDuplicateQueryTests
{
    [Theory]
    [InlineData("Test", "XTEST")]
    [InlineData("Production", "COB")]
    public void BothChecksAreSchemaWideAndUseBoxOnly(string name, string schema)
    {
        Assert.True(EnvironmentSelector.TryResolve(name, out var environment));
        var read = BoxDuplicateQuery.LabelProjection(environment);
        var write = BoxDuplicateQuery.LockedExists(environment);
        Assert.Contains($"[{schema}].[XCOUTD]", read);
        Assert.Contains($"[{schema}].[XCOUTD]", write);
        Assert.Contains("D.BOXNUM_0=L.PID_0", read);
        Assert.Contains("D.BOXNUM_0=@BoxNumber", write);
        foreach (var sql in new[] { read, write })
        {
            Assert.DoesNotContain("CUNNUM_0", sql);
            Assert.DoesNotContain("@CountNumber", sql);
            Assert.DoesNotContain("XCOUTH", sql);
            Assert.DoesNotContain("CUNSTA_0", sql);
            Assert.DoesNotContain("STOFCY_0", sql);
            Assert.DoesNotContain("WRH_0", sql);
        }
        Assert.Contains("AS AlreadyRecordedInCount", read);
        Assert.Contains("UPDLOCK,HOLDLOCK", write);
    }

    [Fact]
    public void LockIdentityIsBoxAndSchemaOnly()
    {
        Assert.True(EnvironmentSelector.TryResolve("Test", out var test));
        Assert.True(EnvironmentSelector.TryResolve("Production", out var production));
        // Independent submissions on different counts get the same resource.
        var first = BoxDuplicateQuery.LockResource(test, "1788360948773|9");
        Assert.Equal(first, BoxDuplicateQuery.LockResource(test, "1788360948773|9"));
        Assert.NotEqual(first, BoxDuplicateQuery.LockResource(test, "1788360948773|10"));
        Assert.NotEqual(first, BoxDuplicateQuery.LockResource(production, "1788360948773|9"));
        Assert.StartsWith("StockCount:Box:", first);
    }

    [Fact]
    public void UnapprovedSchemaCannotProduceSqlOrLock()
    {
        var environment = new SelectedEnvironment("Test", "dbo");
        Assert.Throws<InvalidOperationException>(() => BoxDuplicateQuery.LabelProjection(environment));
        Assert.Throws<InvalidOperationException>(() => BoxDuplicateQuery.LockedExists(environment));
        Assert.Throws<InvalidOperationException>(() => BoxDuplicateQuery.LockResource(environment, "box"));
    }
}
