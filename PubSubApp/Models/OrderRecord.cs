using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PubSubApp.Models;

// An OrderRecord is an object meant for direct translation to a SQL command against the RIMSLF table in MMS
public class OrderRecord
{
    [JsonPropertyName("SLFTTP")]
    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string? TransType { get; set; }

    [JsonPropertyName("SLFLNT")]
    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string? LineType { get; set; }

    [JsonPropertyName("SLFTDT")]
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string? TransDate { get; set; }

    [JsonPropertyName("SLFTTM")]
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string? TransTime { get; set; }

    [JsonPropertyName("SLFCLK")]
    [StringLength(5, MinimumLength = 5)]
    public string? Clerk { get; set; }

    [JsonPropertyName("SLFREG")]
    [StringLength(3, MinimumLength = 3)]
    public string? RegisterID { get; set; }

    [JsonPropertyName("SLFTTX")]
    [StringLength(5, MinimumLength = 5)]
    public string? TransNumber { get; set; }

    [JsonPropertyName("SLFTSQ")]
    [StringLength(5, MinimumLength = 5)]
    public string? TransSeq { get; set; }

    [JsonPropertyName("SLFSKU")]
    [StringLength(9, MinimumLength = 9)]
    public string? SKUNumber { get; set; }

    [JsonPropertyName("SLFQTY")]
    [StringLength(9, MinimumLength = 9)]
    public string? Quantity { get; set; }

    [JsonPropertyName("SLFQTN")]
    [StringLength(1, MinimumLength = 1)]
    public string? QuantityNegativeSign { get; set; }

    [JsonPropertyName("SLFORG")]
    [StringLength(9, MinimumLength = 9)]
    public string? OriginalPrice { get; set; }

    [JsonPropertyName("SLFORN")]
    [StringLength(1, MinimumLength = 1)]
    public string? OriginalPriceNegativeSign { get; set; }

    [JsonPropertyName("SLFADC")]
    [StringLength(4, MinimumLength = 4)]
    public string? AdCode { get; set; }

    [JsonPropertyName("SLFADP")]
    [StringLength(9, MinimumLength = 9)]
    public string? AdPrice { get; set; }

    [JsonPropertyName("SLFADN")]
    [StringLength(1, MinimumLength = 1)]
    public string? AdPriceNegativeSign { get; set; }

    [JsonPropertyName("SLFOVR")]
    [StringLength(9, MinimumLength = 9)]
    public string? OverridePrice { get; set; }

    [JsonPropertyName("SLFOVN")]
    [StringLength(1, MinimumLength = 1)]
    public string? OverridePriceNegativeSign { get; set; }

    [JsonPropertyName("SLFDST")]
    [StringLength(2, MinimumLength = 2)]
    public string? DiscountType { get; set; }

    [JsonPropertyName("SLFDSA")]
    [StringLength(9, MinimumLength = 9)]
    public string? DiscountAmount { get; set; }

    [JsonPropertyName("SLFDSN")]
    [StringLength(1, MinimumLength = 1)]
    public string? DiscountAmountNegativeSign { get; set; }

    [JsonPropertyName("SLFSEL")]
    [StringLength(9, MinimumLength = 9)]
    public string? ItemSellPrice { get; set; }

    [JsonPropertyName("SLFSLN")]
    [StringLength(1, MinimumLength = 1)]
    public string? SellPriceNegativeSign { get; set; }

    [JsonPropertyName("SLFEXT")]
    [StringLength(11, MinimumLength = 11)]
    public string? ExtendedValue { get; set; }

    [JsonPropertyName("SLFEXN")]
    [StringLength(1, MinimumLength = 1)]
    public string? ExtendedValueNegativeSign { get; set; }

    [JsonPropertyName("SLFTX1")]
    [StringLength(1, MinimumLength = 1)]
    public string? ChargedTax1 { get; set; }

    [JsonPropertyName("SLFTX2")]
    [StringLength(1, MinimumLength = 1)]
    public string? ChargedTax2 { get; set; }

    [JsonPropertyName("SLFTX3")]
    [StringLength(1, MinimumLength = 1)]
    public string? ChargedTax3 { get; set; }

    [JsonPropertyName("SLFTX4")]
    [StringLength(1, MinimumLength = 1)]
    public string? ChargedTax4 { get; set; }

    [JsonPropertyName("SLFRFC")]
    [StringLength(1, MinimumLength = 1)]
    public string? ReferenceCode { get; set; }

    [JsonPropertyName("SLFRFD")]
    [StringLength(16, MinimumLength = 16)]
    public string? ReferenceDesc { get; set; }

    [JsonPropertyName("SLFUPC")]
    [StringLength(13, MinimumLength = 13)]
    public string? UPCCode { get; set; }

    [JsonPropertyName("SLFSPS")]
    [StringLength(5, MinimumLength = 5)]
    public string? SalesPerson { get; set; }

    [JsonPropertyName("SLFSTS")]
    [StringLength(1, MinimumLength = 1)]
    public string? Status { get; set; }

    [JsonPropertyName("SLFZIP")]
    [StringLength(10, MinimumLength = 10)]
    public string? ZipCode { get; set; }

    [JsonPropertyName("SLFCNM")]
    [StringLength(35, MinimumLength = 35)]
    public string? CustomerName { get; set; }

    [JsonPropertyName("SLFNUM")]
    [StringLength(10, MinimumLength = 10)]
    public string? CustomerNumber { get; set; }

    [JsonPropertyName("SLFRSN")]
    [StringLength(16, MinimumLength = 16)]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("SLFOSP")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalSalesperson { get; set; }

    [JsonPropertyName("SLFOST")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalStore { get; set; }

    [JsonPropertyName("SLFGDA")]
    [StringLength(9, MinimumLength = 9)]
    public string? GroupDiscAmount { get; set; }

    [JsonPropertyName("SLFGDS")]
    [StringLength(1, MinimumLength = 1)]
    public string? GroupDiscSign { get; set; }

    [JsonPropertyName("SLFGDR")]
    [StringLength(2, MinimumLength = 2)]
    public string? GroupDiscReason { get; set; }

    [JsonPropertyName("SLFRDR")]
    [StringLength(2, MinimumLength = 2)]
    public string? RegDiscReason { get; set; }

    [JsonPropertyName("SLFSCN")]
    [StringLength(1, MinimumLength = 1)]
    public string? ItemScanned { get; set; }

    [JsonPropertyName("SLFACD")]
    [StringLength(6, MinimumLength = 6)]
    public string? TaxAuthCode { get; set; }

    [JsonPropertyName("SLFTCD")]
    [StringLength(6, MinimumLength = 6)]
    public string? TaxRateCode { get; set; }

    [JsonPropertyName("SLFTE1")]
    [StringLength(20, MinimumLength = 20)]
    public string? TaxExemptId1 { get; set; }

    [JsonPropertyName("SLFTE2")]
    [StringLength(20, MinimumLength = 20)]
    public string? TaxExemptId2 { get; set; }

    [JsonPropertyName("SLFTEN")]
    [StringLength(35, MinimumLength = 35)]
    public string? TaxExemptionName { get; set; }

    [JsonPropertyName("SLFPVC")]
    [StringLength(4, MinimumLength = 4)]
    public string? PriceVehicleCode { get; set; }

    [JsonPropertyName("SLFREF")]
    [StringLength(12, MinimumLength = 12)]
    public string? PriceVehicleReference { get; set; }

    [JsonPropertyName("SLFORT")]
    [StringLength(9, MinimumLength = 9)]
    public string? OriginalRetail { get; set; }

    [JsonPropertyName("SLFOPN")]
    [StringLength(1, MinimumLength = 1)]
    public string? OriginalRetailNegativeSign { get; set; }

    [JsonPropertyName("SLFOTS")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalTxStore { get; set; }

    [JsonPropertyName("SLFOTD")]
    [StringLength(6, MinimumLength = 6)]
    public string? OriginalTxDate { get; set; }

    [JsonPropertyName("SLFOTR")]
    [StringLength(3, MinimumLength = 3)]
    public string? OriginalTxRegister { get; set; }

    [JsonPropertyName("SLFOTT")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalTxNumber { get; set; }

    [JsonPropertyName("SLFPLC")]
    [Range(0, 99999)]
    public int? PolledStore { get; set; }

    [JsonPropertyName("SLFPCN")]
    [Range(0, 9)]
    public int? PollCen { get; set; }

    [JsonPropertyName("SLFPDT")]
    [Range(0, 999999)]
    public int? PollDate { get; set; }

    [JsonPropertyName("SLFCCN")]
    [Range(0, 9)]
    public int? CreateCen { get; set; }

    [JsonPropertyName("SLFCDT")]
    [Range(0, 999999)]
    public int? CreateDate { get; set; }

    [JsonPropertyName("SLFCTM")]
    [Range(0, 999999)]
    public int? CreateTime { get; set; }

    [JsonPropertyName("SLFORD")]
    [StringLength(8, MinimumLength = 8)]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("SLFSIL")]
    [Range(0, 99999)]
    public int? SmartLocation { get; set; }

    [JsonPropertyName("ASFPRO")]
    [StringLength(5, MinimumLength = 5)]
    public string? ProjectNumber { get; set; }

    [JsonPropertyName("ASFCST")]
    [StringLength(9, MinimumLength = 9)]
    public string? OriginalCost { get; set; }

    [JsonPropertyName("ASFSST")]
    [Range(0, 99999)]
    public int? SalesStore { get; set; }

    [JsonPropertyName("ASFIST")]
    [Range(0, 99999)]
    public int? InvStore { get; set; }

    [JsonPropertyName("ASFSTY")]
    [StringLength(1, MinimumLength = 1)]
    public string? SalesType { get; set; }

    [JsonPropertyName("ASFISP")]
    [StringLength(5, MinimumLength = 5)]
    public string? InsideSalesPerson { get; set; }

    [JsonPropertyName("ASFOSP")]
    [StringLength(5, MinimumLength = 5)]
    public string? OutsideSalesPerson { get; set; }

    [JsonPropertyName("ASFOTX")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalTransNumber { get; set; }

    [JsonPropertyName("ASFMBT")]
    [StringLength(1, MinimumLength = 1)]
    public string? CustomerType { get; set; }

    [JsonPropertyName("SLFEML")]
    [StringLength(60, MinimumLength = 60)]
    public string? EReceiptEmail { get; set; }

    [JsonPropertyName("SLFECN")]
    [Range(0, 9999999999999)]
    public long? EmployeeCardNumber { get; set; }

    // Non-serialized tracking fields for console output
    [JsonIgnore]
    public string? SourceLineId { get; set; }

    [JsonIgnore]
    public string? SourceParentLineId { get; set; }
}
