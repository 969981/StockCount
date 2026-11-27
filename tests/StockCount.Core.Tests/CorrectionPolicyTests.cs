using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CorrectionPolicyTests
{
    [Theory]
    [InlineData(0,false,false,false,false,"New")]
    [InlineData(1,true,true,true,true,"Update")]
    [InlineData(1,true,false,true,true,"LocationConflict")]
    [InlineData(1,false,true,true,true,"CountConflict")]
    [InlineData(2,true,true,true,true,"Ambiguous")]
    [InlineData(1,true,true,false,true,"Unsupported")]
    [InlineData(1,true,true,true,false,"UpdateDisabled")]
    public void ClassifiesWithoutCrossCountUpdate(int n,bool count,bool loc,bool compatible,bool enabled,string mode) =>
        Assert.Equal(mode,CorrectionPolicy.Classify(n,count,loc,compatible,enabled));

    [Fact]
    public void SnapshotBindsExactCountLocationAndBox()
    {
        var snapshot = new BoxCorrection("Update","123","C1","B1","L1",10m,"KG",null,new string('A',64),null);
        Assert.True(CorrectionPolicy.Matches(snapshot,"C1","L1","B1"));
        Assert.False(CorrectionPolicy.Matches(snapshot,"C2","L1","B1"));
        Assert.False(CorrectionPolicy.Matches(snapshot,"C1","L2","B1"));
        Assert.False(CorrectionPolicy.Matches(snapshot,"C1","L1","B2"));
        Assert.False(CorrectionPolicy.Matches(snapshot with { Version = "bad" },"C1","L1","B1"));
    }
}
