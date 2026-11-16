using System.Text.Json;
using FluentAssertions;
using PubSubApp.Models;
using PubSubApp.Validation;
using Xunit;

namespace PubSubApp.Tests;

/// MIM-11789: a void gift card (order.externalIds TACTILL / VOID_GC_ORDER) writes TNFTTP "11" and
/// TNFAUT = the line's own TNFAMT on both RIMTNF tender lines, with no RIMSLF lines.
public class VoidGiftCardTests
{
    private static string SamplePath => Path.Combine(SampleCase.SamplesRoot, "Void GC", "void_gc_order.json");

    private static RecordSet Map(Action<JsonElementEdit>? edit = null)
    {
        string json = File.ReadAllText(SamplePath);
        if (edit != null)
        {
            var e = new JsonElementEdit(json);
            edit(e);
            json = e.Json;
        }

        var mapper = new RetailEventMapper();
        return mapper.MapRetailEventToRecordSet(mapper.ReadRecordSetFromString(json));
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCard_BothTenderLinesAreType11()
    {
        RecordSet result = Map();

        result.TenderRecords.Should().HaveCount(2);
        result.TenderRecords.Select(t => t.TransactionType).Should().OnlyContain(t => t == "11");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCard_AuthNumberEqualsEachLinesAmount()
    {
        RecordSet result = Map();

        result.TenderRecords.Should().OnlyContain(t => t.Amount == "00000004500" && t.AuthNumber == "004500");
        result.TenderRecords.Select(t => t.AmountNegativeSign).Should().Equal("-", "");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCard_CreditLineWithZeroAuthCodeGetsAmountAsAuth()
    {
        // The sample's credit line carries authCode 000000; TNFAUT must still be 004500.
        RecordSet result = Map();

        TenderRecord credit = result.TenderRecords.Single(t => t.AmountNegativeSign == "");
        credit.AuthNumber.Should().Be("004500");
        credit.TransactionType.Should().Be("11");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCard_HasNoSalesLinesAndKeepsGiftCardFundCode()
    {
        RecordSet result = Map();

        result.OrderRecords.Should().BeEmpty();
        result.TenderRecords.Should().OnlyContain(t => t.FundCode == "PG");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCard_OutputPassesValidation()
    {
        RecordSetValidator.ValidateRecordSetOutput(Map()).Should().BeEmpty();
    }

    [Fact]
    public void MapRetailEventToRecordSet_SameOrderWithoutVoidMarker_KeepsNormalTenderFields()
    {
        RecordSet result = Map(e => e.Remove("order"));

        result.TenderRecords.Select(t => t.TransactionType).Should().NotContain("11");
        result.TenderRecords.Select(t => t.AuthNumber).Should().Equal("812345", "000000");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCardMarkerIsCaseInsensitive()
    {
        RecordSet result = Map(e => e.Replace("VOID_GC_ORDER", "void_gc_order"));

        result.TenderRecords.Should().OnlyContain(t => t.TransactionType == "11");
    }

    [Fact]
    public void MapRetailEventToRecordSet_VoidGiftCardAt10000Dollars_ClampsAuthNumberTo999999()
    {
        RecordSet result = Map(e => e.Replace("\"-45.00\"", "\"-10000.00\"").Replace("\"45.00\"", "\"10000.00\""));

        result.TenderRecords.Select(t => t.Amount).Should().OnlyContain(a => a == "00001000000");
        result.TenderRecords.Select(t => t.AuthNumber).Should().OnlyContain(a => a == "999999");
    }

    /// Tiny text-level editor so each test varies the one sample without a second file.
    public sealed class JsonElementEdit
    {
        public string Json { get; private set; }

        public JsonElementEdit(string json) => Json = json;

        public JsonElementEdit Replace(string from, string to) { Json = Json.Replace(from, to); return this; }

        public JsonElementEdit Remove(string property)
        {
            using JsonDocument doc = JsonDocument.Parse(Json);
            var dict = new Dictionary<string, JsonElement>();
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                if (p.Name != property)
                    dict[p.Name] = p.Value.Clone();
            Json = JsonSerializer.Serialize(dict);
            return this;
        }
    }
}
