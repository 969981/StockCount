using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CountScanWorkflowTests
{
    private const string Qr="ID.1794814541901|1@@PT.PTHG01260700005@@LN.1000@@MT.A10000087@@LT.L26070146@@QT.100@@PG.100";

    [Fact]
    public async Task BoxBeforeLocationShowsPromptWithoutFetchingLabel()
    {
        var fake=new FakeGateway();
        var workflow=new CountScanWorkflow(fake,"1712474746529","789号仓库辅料盘点");
        var result=await workflow.HandleScanAsync(Qr);
        Assert.Equal("请先扫描盘点库位！",result.Message);
        Assert.Equal(0,fake.LabelRequests);
        Assert.Null(workflow.Box);
    }

    [Fact]
    public async Task DuplicateBoxDoesNotPopulateAnyBoxField()
    {
        var fake=new FakeGateway { Duplicate=true };
        var workflow=new CountScanWorkflow(fake,"1712474746529","测试");
        await workflow.HandleScanAsync("0700Q0001");
        var result=await workflow.HandleScanAsync(Qr);
        Assert.Equal("该箱号已扫描！",result.Message);
        Assert.Null(workflow.Box);
        Assert.Equal("0700Q0001",workflow.Location);
    }

    [Fact]
    public async Task AfterSuccessfulSubmissionKeepOrderAndLocationClearBoxOnly()
    {
        var fake=new FakeGateway();
        var workflow=new CountScanWorkflow(fake,"1712474746529","789号仓库辅料盘点");
        await workflow.HandleScanAsync("0700Q0001");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("Submitted",result.Kind);
        Assert.Equal("提交成功！",result.Message);
        Assert.Equal(1000,result.AutoDismissMs);
        Assert.Null(workflow.Box);
        Assert.Null(workflow.ManualQuantity);
        Assert.Null(workflow.PendingRequestId);
        Assert.Equal("0700Q0001",workflow.Location);
        Assert.Equal("1712474746529",workflow.CountNumber);
        Assert.Equal(99m,fake.LastSubmittedQuantity);
    }

    [Fact]
    public async Task UnknownOutcomePreservesDraftAndRequestId()
    {
        var fake=new FakeGateway { ThrowDuringSubmit=true };
        var workflow=new CountScanWorkflow(fake,"1712474746529","测试");
        await workflow.HandleScanAsync("0700Q0001");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("Unknown",result.Kind);
        Assert.NotNull(workflow.PendingRequestId);
        var requestId=workflow.PendingRequestId;
        Assert.Equal("1794814541901|1",workflow.Box?.BoxNumber);
        Assert.Equal(99m,workflow.ManualQuantity);
        Assert.Throws<InvalidOperationException>(()=>workflow.EnterManualQuantity(100m));
        fake.ThrowDuringSubmit=false;
        var retry=await workflow.SubmitAsync();
        Assert.Equal("Submitted",retry.Kind);
        Assert.Equal(requestId,fake.LastRequestId);
    }

    [Fact]
    public async Task ExplicitConcurrentDuplicateReturnsBusinessPopup()
    {
        var fake=new FakeGateway { RejectDuplicateAtCommit=true };
        var workflow=new CountScanWorkflow(fake,"1712474746529","测试");
        await workflow.HandleScanAsync("0700Q0001");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("该箱号已扫描！",result.Message);
        Assert.Null(workflow.Box);
        Assert.Equal("0700Q0001",workflow.Location);
    }

    [Fact]
    public async Task RescanningDifferentLocationClearsPreviousBoxButRetainsCount()
    {
        var workflow=new CountScanWorkflow(new FakeGateway(),"1712474746529","测试");
        await workflow.HandleScanAsync("0700Q0001");
        await workflow.HandleScanAsync(Qr);
        var result=await workflow.HandleScanAsync("0700Q0002");
        Assert.Equal("LocationUpdated",result.Kind);
        Assert.Equal("0700Q0002",workflow.Location);
        Assert.Null(workflow.Box);
        Assert.Equal("1712474746529",workflow.CountNumber);
    }

    private sealed class FakeGateway : ICountScanGateway
    {
        public bool Duplicate { get; set; }
        public bool RejectDuplicateAtCommit { get; set; }
        public bool ThrowDuringSubmit { get; set; }
        public int LabelRequests { get; private set; }
        public decimal? LastSubmittedQuantity { get; private set; }
        public Guid? LastRequestId { get; private set; }
        public Task<string?> ResolveLocationAsync(string countNumber,string rawCode,CancellationToken ct) =>
            Task.FromResult<string?>(rawCode is "0700Q0001" or "0700Q0002" ? rawCode : null);
        public Task<BoxScanData?> ResolveLabelAsync(string countNumber,string qr,CancellationToken ct)
        {
            LabelRequests++;
            return Task.FromResult<BoxScanData?>(new BoxScanData("1794814541901|1",
                "A10000087","PCD刀片","TNGA160404 PCD-010","L26070146","PCS",Duplicate));
        }

        public Task<bool> SubmitAsync(string countNumber,Guid requestId,string qr,
            string boxNumber,string location,decimal qty,CancellationToken ct)
        {
            LastRequestId=requestId;
            if (RejectDuplicateAtCommit)
                throw new ScanCommitRejectedException("BOX_ALREADY_RECORDED","该箱号已扫描！");
            if (ThrowDuringSubmit) throw new IOException("Simulated dropped connection");
            LastSubmittedQuantity=qty;
            return Task.FromResult(true);
        }
        public Task<bool?> GetSubmissionAsync(string countNumber,Guid requestId,CancellationToken ct) =>
            Task.FromResult<bool?>(LastRequestId==requestId ? true : false);
    }
}
