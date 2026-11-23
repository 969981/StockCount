using StockCount.Core;
using Xunit;

namespace StockCount.Core.Tests;

public class CountWritePolicyTests
{
    private const string Count = "COUNT-DEMO-001";
    private const string Qr = "ID.BOX-DEMO-001@@PT.DOC-DEMO-001@@LN.1000@@MT.ITM-DEMO-01@@LT.LOT-01@@QT.100@@PG.100";
    private const string Box = "BOX-DEMO-001";
    private const string Location = "LOC-DEMO01";

    [Fact]
    public void ManualCount99IsValidEvenWhenQrHasQt100() =>
        Assert.Null(CountWritePolicy.Validate(Count,Qr,Box,Location,99m));

    [Fact]
    public void ZeroIsDistinguishableFromMissingAndAcceptedByConservativePolicy() =>
        Assert.Null(CountWritePolicy.Validate(Count,Qr,Box,Location,0m));

    [Theory]
    [InlineData(-1.0)]
    [InlineData(9999999999999999.0)]
    public void RejectInvalidQuantities(double value) =>
        Assert.Equal("COUNT_QUANTITY_INVALID",
            CountWritePolicy.Validate(Count,Qr,Box,Location,(decimal)value));

    [Fact]
    public void RejectTooLongBox() =>
        Assert.Equal("BOX_NUMBER_TOO_LONG",
            CountWritePolicy.Validate(Count,Qr,new string('X',21),Location,99m));

    [Fact]
    public void RejectMissingLocation() =>
        Assert.Equal("LOCATION_INVALID",
            CountWritePolicy.Validate(Count,Qr,Box,"",99m));

    [Fact]
    public void RejectExtraDecimalPlaces() =>
        Assert.Equal("COUNT_QUANTITY_INVALID",
            CountWritePolicy.Validate(Count,Qr,Box,Location,0.00000000000001m));

    [Fact]
    public void SameOperationAndDifferentDecimalRepresentationsHashIdentically()
    {
        var first = CountWritePolicy.PayloadHash("Test",Count,Qr,Box,Location,99m);
        var replay = CountWritePolicy.PayloadHash("Test",Count,Qr,Box,Location,99.000m);
        Assert.Equal(first,replay);
    }

    [Fact]
    public void DifferentPayloadOrEnvironmentProducesDifferentKeyHash()
    {
        var first = CountWritePolicy.PayloadHash("Test",Count,Qr,Box,Location,99m);
        var differentQty = CountWritePolicy.PayloadHash("Test",Count,Qr,Box,Location,100m);
        var otherSchema = CountWritePolicy.PayloadHash("Production",Count,Qr,Box,Location,99m);
        Assert.NotEqual(first,differentQty);
        Assert.NotEqual(first,otherSchema);
    }

    [Theory]
    [InlineData("PCS","PCS",1.0,true)]
    [InlineData("PCS","BOX",1.0,false)]
    [InlineData("PCS","PCS",10.0,false)]
    [InlineData("","",1.0,false)]
    public void UnitsMustBeOneToOne(string stu,string pcu,double factor,bool expected) =>
        Assert.Equal(expected,CountWritePolicy.SupportedUnitMapping(stu,pcu,(decimal)factor));
}
