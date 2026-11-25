using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public sealed class CountScanWorkflowTests
{
    private const string Qr="ID.BOX-DEMO-001@@PT.DOC-DEMO-001@@LN.1000@@MT.ITM-DEMO-01@@LT.LOT-01@@QT.100@@PG.100";

    [Fact]
    public async Task BoxBeforeLocationShowsPromptWithoutFetchingLabel()
    {
        var fake=new FakeGateway();
        var workflow=new CountScanWorkflow(fake,"COUNT-DEMO-001","测试盘点单");
        var result=await workflow.HandleScanAsync(Qr);
        Assert.Equal("请先扫描盘点库位！",result.Message);
        Assert.Equal(0,fake.LocationRequests);
        Assert.Equal(0,fake.LabelRequests);
        Assert.Null(workflow.Box);
    }

    [Theory]
    [InlineData("01234567890")]
    [InlineData("LC.01234567890")]
    [InlineData("LC.")]
    [InlineData("LC.LC.DEMO")]
    [InlineData("LC.A@@MT.B")]
    [InlineData("MT.DEMO")]
    [InlineData("LOC\t001")]
    public async Task InvalidLocationFormatDoesNotQueryDatabase(string raw)
    {
        var fake = new FakeGateway();
        var workflow = new CountScanWorkflow(fake, "COUNT-DEMO-001", "测试");
        var feedback = await workflow.HandleScanAsync(raw);
        Assert.Equal("Error", feedback.Kind);
        Assert.Contains("库位编码格式不符合要求", feedback.Message);
        Assert.Equal(0, fake.LocationRequests);
        Assert.Null(workflow.Location);
    }

    [Theory]
    [InlineData("LOCATION_NOT_FOUND", "当前帐套不存在该库位")]
    [InlineData("LOCATION_OUTSIDE_WAREHOUSE", "不属于当前盘点单")]
    [InlineData("LOCATION_AMBIGUOUS", "重复库位记录")]
    [InlineData("LOCATION_LOOKUP_UNAVAILABLE", "库位查询失败")]
    [InlineData("COUNT_NOT_FOUND", "当前盘点单不存在")]
    [InlineData("LOCATION_INVALID", "旧版后台")]
    public async Task LocationLookupFailureExplainsReasonAndKeepsCurrentLocation(string code, string expected)
    {
        var fake = new FakeGateway { LocationError = code };
        var workflow = new CountScanWorkflow(fake, "COUNT-DEMO-001", "测试");
        var initial = await workflow.HandleScanAsync("UNKNOWN");
        Assert.Equal("Error", initial.Kind);
        Assert.Contains(expected, initial.Message);
        Assert.Null(workflow.Location);
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        var later = await workflow.HandleScanAsync("UNKNOWN");
        Assert.Equal("Error", later.Kind);
        Assert.Contains(expected, later.Message);
        Assert.Equal("LOC-DEMO01", workflow.Location);
        Assert.NotNull(workflow.Box);
    }

    [Fact]
    public async Task LcBarcodeDisplaysAndSubmitsOnlyResolvedLocation()
    {
        var fake = new FakeGateway();
        var workflow = new CountScanWorkflow(fake, "COUNT-DEMO-001", "测试");
        Assert.Equal("LocationUpdated", (await workflow.HandleScanAsync("LC.LOC-DEMO01")).Kind);
        Assert.Equal("LC.LOC-DEMO01", fake.LastRawLocation);
        Assert.Equal("LOC-DEMO01", workflow.Location);
        Assert.Equal("BoxResolved", (await workflow.HandleScanAsync(Qr)).Kind);
        workflow.EnterManualQuantity(9m);
        Assert.Equal("Submitted", (await workflow.SubmitAsync()).Kind);
        Assert.Equal("LOC-DEMO01", fake.LastSubmittedLocation);
        await workflow.AcknowledgeSuccessAsync();
        Assert.Equal("LOC-DEMO01", workflow.Location);
    }

    [Fact]
    public async Task DuplicateBoxDoesNotPopulateAnyBoxField()
    {
        var fake=new FakeGateway { Duplicate=true };
        var workflow=new CountScanWorkflow(fake,"COUNT-DEMO-001","测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        var result=await workflow.HandleScanAsync(Qr);
        Assert.Equal("该箱号已扫描！",result.Message);
        Assert.Null(workflow.Box);
        Assert.Equal("LOC-DEMO01",workflow.Location);
    }

    [Fact]
    public async Task AfterSuccessfulSubmissionKeepOrderAndLocationClearBoxOnly()
    {
        var fake=new FakeGateway();
        var workflow=new CountScanWorkflow(fake,"COUNT-DEMO-001","测试盘点单");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("Submitted",result.Kind);
        Assert.Equal("提交成功！",result.Message);
        Assert.Equal(1000,result.AutoDismissMs);
        // The approved UI waits for its 1000 ms success display before clearing.
        Assert.True(workflow.IsSuccessAwaitingDismiss);
        Assert.NotNull(workflow.Box);
        Assert.Equal(99m, workflow.ManualQuantity);
        await workflow.AcknowledgeSuccessAsync();
        Assert.Null(workflow.Box);
        Assert.Null(workflow.ManualQuantity);
        Assert.Null(workflow.PendingRequestId);
        Assert.Equal("LOC-DEMO01",workflow.Location);
        Assert.Equal("COUNT-DEMO-001",workflow.CountNumber);
        Assert.Equal(99m,fake.LastSubmittedQuantity);
    }

    [Fact]
    public async Task UnknownOutcomePreservesDraftAndRequestId()
    {
        var fake=new FakeGateway { ThrowDuringSubmit=true };
        var workflow=new CountScanWorkflow(fake,"COUNT-DEMO-001","测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("Unknown",result.Kind);
        Assert.NotNull(workflow.PendingRequestId);
        var requestId=workflow.PendingRequestId;
        Assert.Equal("BOX-DEMO-001",workflow.Box?.BoxNumber);
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
        var workflow=new CountScanWorkflow(fake,"COUNT-DEMO-001","测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);
        var result=await workflow.SubmitAsync();
        Assert.Equal("该箱号已扫描！",result.Message);
        Assert.Null(workflow.Box);
        Assert.Equal("LOC-DEMO01",workflow.Location);
    }

    [Fact]
    public async Task RescanningDifferentLocationClearsPreviousBoxButRetainsCount()
    {
        var workflow=new CountScanWorkflow(new FakeGateway(),"COUNT-DEMO-001","测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        var result=await workflow.HandleScanAsync("LOC-DEMO02");
        Assert.Equal("LocationUpdated",result.Kind);
        Assert.Equal("LOC-DEMO02",workflow.Location);
        Assert.Null(workflow.Box);
        Assert.Equal("COUNT-DEMO-001",workflow.CountNumber);
    }

    [Fact]
    public async Task ScanningAnotherBoxWithEnteredQuantityMustNotDiscardDraft()
    {
        var workflow = new CountScanWorkflow(new FakeGateway(), "COUNT-DEMO-001", "测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        workflow.EnterManualQuantity(99m);

        var response = await workflow.HandleScanAsync("LOC-DEMO02");
        Assert.Equal("DraftNeedsConfirmation", response.Kind);
        Assert.Equal("LOC-DEMO01", workflow.Location);
        Assert.Equal("BOX-DEMO-001", workflow.Box?.BoxNumber);
        Assert.Equal(99m, workflow.ManualQuantity);

        Assert.True(workflow.DiscardUnsubmittedDraft());
        var switched = await workflow.HandleScanAsync("LOC-DEMO02");
        Assert.Equal("LocationUpdated", switched.Kind);
        Assert.Equal("LOC-DEMO02", workflow.Location);
    }

    [Fact]
    public async Task NetworkUnknownPreservesEncryptedStorePayloadForAppRestart()
    {
        var memory = new MemoryStore();
        var fake = new FakeGateway { ThrowDuringSubmit = true };
        var first = new CountScanWorkflow(fake,"COUNT-DEMO-001","测试","Test",memory);
        await first.HandleScanAsync("LOC-DEMO01");
        await first.HandleScanAsync(Qr);
        first.EnterManualQuantity(99m);
        Assert.Equal("Unknown",(await first.SubmitAsync()).Kind);

        var uuid = first.PendingRequestId;
        Assert.NotNull(uuid);
        Assert.Equal(uuid, memory.Draft?.RequestId);

        var resumed = new CountScanWorkflow(fake,"COUNT-DEMO-001","测试","Test",memory);
        Assert.True(await resumed.RestorePendingAsync());
        Assert.Equal(uuid,resumed.PendingRequestId);
        Assert.Equal(99m,resumed.ManualQuantity);
        Assert.Equal("LOC-DEMO01",resumed.Location);
        Assert.False(resumed.DiscardUnsubmittedDraft());

        // The server processed the request although the original reply was lost.
        Assert.Equal("Submitted",(await resumed.CheckPendingAsync()).Kind);
        Assert.NotNull(memory.Draft); // Keep until success display has completed.
        await resumed.AcknowledgeSuccessAsync();
        Assert.Null(memory.Draft);
        Assert.Null(resumed.Box);
        Assert.Equal("LOC-DEMO01",resumed.Location);
    }

    [Fact]
    public async Task MissingQuantityCannotSubmitOrGenerateRequestId()
    {
        var fake = new FakeGateway();
        var workflow = new CountScanWorkflow(fake,"COUNT-DEMO-001","测试");
        await workflow.HandleScanAsync("LOC-DEMO01");
        await workflow.HandleScanAsync(Qr);
        var result=await workflow.SubmitAsync();
        Assert.Equal("箱内数量为必填项！",result.Message);
        Assert.Null(workflow.PendingRequestId);
    }

    [Fact]
    public async Task PendingSubmissionCannotBeRestoredUnderAnotherEnvironment()
    {
        var store = new MemoryStore();
        var fake = new FakeGateway { ThrowDuringSubmit = true };
        var test = new CountScanWorkflow(fake, "COUNT-DEMO-001", "测试", "Test", store);
        await test.HandleScanAsync("LOC-DEMO01");
        await test.HandleScanAsync(Qr);
        test.EnterManualQuantity(99m);
        Assert.Equal("Unknown", (await test.SubmitAsync()).Kind);

        var production = new CountScanWorkflow(fake, "COUNT-DEMO-001", "测试",
            "Production", store);
        Assert.False(await production.RestorePendingAsync());
        Assert.Null(production.Box);
        Assert.Equal("Test", store.Draft?.Environment); // Never clear someone else's draft.
        Assert.NotNull(store.Draft?.RequestId);
    }

    private sealed class MemoryStore : IPendingSubmissionStore
    {
        public PendingSubmissionDraft? Draft { get; private set; }
        public Task SaveAsync(PendingSubmissionDraft draft,CancellationToken ct)
        {
            Draft=draft;
            return Task.CompletedTask;
        }
        public Task<PendingSubmissionDraft?> LoadAsync(CancellationToken ct) =>
            Task.FromResult(Draft);
        public Task ClearAsync(CancellationToken ct)
        {
            Draft=null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGateway : ICountScanGateway
    {
        public bool Duplicate { get; set; }
        public bool RejectDuplicateAtCommit { get; set; }
        public bool ThrowDuringSubmit { get; set; }
        public int LabelRequests { get; private set; }
        public int LocationRequests { get; private set; }
        public string? LastRawLocation { get; private set; }
        public string? LastSubmittedLocation { get; private set; }
        public string LocationError { get; set; } = "LOCATION_NOT_FOUND";
        public decimal? LastSubmittedQuantity { get; private set; }
        public Guid? LastRequestId { get; private set; }
        public Task<LocationScanResult> ResolveLocationAsync(string countNumber,string rawCode,CancellationToken ct)
        {
            LocationRequests++;
            LastRawLocation = rawCode;
            return Task.FromResult(LocationScanPolicy.TryParse(rawCode, out var location) && location is ("LOC-DEMO01" or "LOC-DEMO02")
                ? new LocationScanResult(location)
                : new LocationScanResult(null, LocationError));
        }
        public Task<BoxScanData?> ResolveLabelAsync(string countNumber,string qr,CancellationToken ct)
        {
            LabelRequests++;
            return Task.FromResult<BoxScanData?>(new BoxScanData("BOX-DEMO-001",
                "ITM-DEMO-01","示例物料","DEMO-SPEC","LOT-01","PCS",Duplicate));
        }

        public Task<bool> SubmitAsync(string countNumber,Guid requestId,string qr,
            string boxNumber,string location,decimal qty,CancellationToken ct)
        {
            LastRequestId=requestId;
            LastSubmittedLocation=location;
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
