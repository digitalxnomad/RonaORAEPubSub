using FluentAssertions;
using PubSubApp.Models;
using Xunit;

namespace PubSubApp.Tests;

/// TACO-75: on a price ADJUSTMENT the return leg carries reason RRT02010, and the sale leg presents
/// as a price override (OVD/OVR, SLFORG = returned price, SLFOVR = adjusted price) with no
/// original-transaction reference.
public class PriceAdjustmentTests
{
    private static List<OrderRecord> SkuLines()
    {
        string json = File.ReadAllText(Path.Combine(SampleCase.SamplesRoot, "Returns", "adjustment_two_skus.json"));
        var mapper = new RetailEventMapper();
        RecordSet result = mapper.MapRetailEventToRecordSet(mapper.ReadRecordSetFromString(json));
        List<OrderRecord> orders = result.OrderRecords
            ?? throw new InvalidOperationException("Mapper returned a RecordSet without OrderRecords.");
        return orders.Where(o => o.LineType is "11" or "01").ToList();
    }

    [Fact]
    public void MapRetailEventToRecordSet_AdjustmentReturnLeg_ReasonIsRrt02010()
    {
        List<OrderRecord> lines = SkuLines();

        lines.Where(o => o.LineType == "11").Select(o => o.ReasonCode?.Trim())
            .Should().Equal("RRT02010", "RRT02010");
    }

    [Fact]
    public void MapRetailEventToRecordSet_AdjustmentSaleLeg_IsPriceOverrideOfReturnedPrice()
    {
        OrderRecord sale = SkuLines().First(o => o.LineType == "01");

        (sale.PriceVehicleCode?.Trim()).Should().Be("OVD");
        (sale.PriceVehicleReference?.Trim()).Should().Be("OVR");
        sale.OriginalPrice.Should().Be("000003679");
        sale.OverridePrice.Should().Be("000003311");
        sale.ItemSellPrice.Should().Be("000003311");
        (sale.ReasonCode?.Trim()).Should().Be("POV01502");
    }

    [Fact]
    public void MapRetailEventToRecordSet_AdjustmentSaleLeg_HasNoOriginalTransaction()
    {
        OrderRecord sale = SkuLines().First(o => o.LineType == "01");

        new[] { sale.OriginalStore, sale.OriginalTxStore, sale.OriginalTxDate, sale.OriginalTxRegister, sale.OriginalTxNumber }
            .Should().Equal("00000", "00000", "000000", "000", "00000");
    }
}
