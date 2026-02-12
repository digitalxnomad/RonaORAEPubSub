using System.Text.Json.Serialization;

namespace PubSubApp.Models;

public class BusinessContext
{
    [JsonPropertyName("businessDay")]
    public DateTime BusinessDay { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("fulfillment")]
    public string? Fulfillment { get; set; }

    [JsonPropertyName("store")]
    public required Store Store { get; set; }

    [JsonPropertyName("workstation")]
    public required Workstation Workstation { get; set; }
}

public class Store
{
    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("taxArea")]
    public string? TaxArea { get; set; }
}

public class Workstation
{
    [JsonPropertyName("registerId")]
    public string? RegisterId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sequenceNumber")]
    public long? SequenceNumber { get; set; }
}

public class References
{
    [JsonPropertyName("sourceTransactionId")]
    public string? SourceTransactionId { get; set; }
}

public class Transaction
{
    [JsonPropertyName("transactionType")]
    public string? TransactionType { get; set; }

    [JsonPropertyName("items")]
    public required List<TransactionItem> Items { get; set; }

    [JsonPropertyName("tenders")]
    public List<Tender>? Tenders { get; set; }

    [JsonPropertyName("totals")]
    public required Totals Totals { get; set; }
}

public class Actor
{
    [JsonPropertyName("cashier")]
    public Cashier? Cashier { get; set; }

    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }
}

public class Customer
{
    [JsonPropertyName("customerIdToken")]
    public string? CustomerIdToken { get; set; }
}

public class Cashier
{
    [JsonPropertyName("loginId")]
    public string? LoginId { get; set; }
}

public class Tender
{
    [JsonPropertyName("tenderId")]
    public string? TenderId { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("amount")]
    public CurrencyAmount? Amount { get; set; }

    [JsonPropertyName("card")]
    public Card? Card { get; set; }
}

public class Card
{
    [JsonPropertyName("scheme")]
    public string? Scheme { get; set; }

    [JsonPropertyName("last4")]
    public string? Last4 { get; set; }

    [JsonPropertyName("authCode")]
    public string? AuthCode { get; set; }

    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; set; }

    [JsonPropertyName("emv")]
    public Emv? Emv { get; set; }
}

public class Emv
{
    [JsonPropertyName("tags")]
    public EmvTags? Tags { get; set; }
}

public class EmvTags
{
    [JsonPropertyName("magStrip")]
    public string? MagStrip { get; set; }
}

public class TransactionItem
{
    [JsonPropertyName("item")]
    public Item? Item { get; set; }

    [JsonPropertyName("lineId")]
    public string? LineId { get; set; }

    [JsonPropertyName("parentLineId")]
    public string? ParentLineId { get; set; }

    [JsonPropertyName("pricing")]
    public Pricing? Pricing { get; set; }

    [JsonPropertyName("quantity")]
    public Quantity? Quantity { get; set; }

    [JsonPropertyName("taxes")]
    public List<TaxDetail>? Taxes { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }
}

public class Item
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("gtin")]
    public string? Gtin { get; set; }
}

public class Pricing
{
    [JsonPropertyName("extendedPrice")]
    public CurrencyAmount? ExtendedPrice { get; set; }

    [JsonPropertyName("originalUnitPrice")]
    public CurrencyAmount? OriginalUnitPrice { get; set; }

    [JsonPropertyName("unitPrice")]
    public CurrencyAmount? UnitPrice { get; set; }

    [JsonPropertyName("override")]
    public CurrencyAmount? Override { get; set; }

    [JsonPropertyName("priceVehicle")]
    public string? PriceVehicle { get; set; }

    [JsonPropertyName("priceOverride")]
    public PriceOverride? PriceOverride { get; set; }
}

public class PriceOverride
{
    [JsonPropertyName("approvedBy")]
    public string? ApprovedBy { get; set; }

    [JsonPropertyName("overridden")]
    public bool? Overridden { get; set; }

    [JsonPropertyName("overrideUnitPrice")]
    public CurrencyAmount? OverrideUnitPrice { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class Quantity
{
    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("uom")]
    public string? Uom { get; set; }

    [JsonPropertyName("randomWeight")]
    public bool? RandomWeight { get; set; }

    [JsonPropertyName("tareWeight")]
    public decimal? TareWeight { get; set; }
}

public class TaxDetail
{
    [JsonPropertyName("jurisdiction")]
    public TaxJurisdiction? Jurisdiction { get; set; }

    [JsonPropertyName("taxType")]
    public string? TaxType { get; set; }

    [JsonPropertyName("taxCategory")]
    public string? TaxCategory { get; set; }

    [JsonPropertyName("taxCode")]
    public string? TaxCode { get; set; }

    [JsonPropertyName("taxAuthority")]
    public string? TaxAuthority { get; set; }

    [JsonPropertyName("taxRate")]
    public decimal? TaxRate { get; set; }

    [JsonPropertyName("ratePercent")]
    public string? RatePercent { get; set; }

    [JsonPropertyName("amount")]
    public CurrencyAmount? TaxAmount { get; set; }

    [JsonPropertyName("taxableAmount")]
    public CurrencyAmount? TaxableAmount { get; set; }

    [JsonPropertyName("taxExempt")]
    public bool? TaxExempt { get; set; }

    [JsonPropertyName("exemptReason")]
    public string? ExemptReason { get; set; }
}

public class TaxJurisdiction
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("locality")]
    public string? Locality { get; set; }

    [JsonPropertyName("authorityId")]
    public string? AuthorityId { get; set; }

    [JsonPropertyName("authorityName")]
    public string? AuthorityName { get; set; }
}

public class Totals
{
    [JsonPropertyName("gross")]
    public CurrencyAmount? Gross { get; set; }

    [JsonPropertyName("discounts")]
    public CurrencyAmount? Discounts { get; set; }

    [JsonPropertyName("tax")]
    public CurrencyAmount? Tax { get; set; }

    [JsonPropertyName("net")]
    public CurrencyAmount? Net { get; set; }

    [JsonPropertyName("tendered")]
    public CurrencyAmount? Tendered { get; set; }

    [JsonPropertyName("changeDue")]
    public CurrencyAmount? ChangeDue { get; set; }

    [JsonPropertyName("cashRounding")]
    public CurrencyAmount? CashRounding { get; set; }
}

public class CurrencyAmount
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }  // Using string? to match JSON, or use decimal if preferred
}
