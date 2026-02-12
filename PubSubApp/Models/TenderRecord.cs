using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PubSubApp.Models;

// A TenderRecord is an object meant for direct translation to a SQL command against the RIMTNF table in MMS
public class TenderRecord
{
    [JsonPropertyName("TNFTTP")]
    [StringLength(2, MinimumLength = 2)]
    public string? TransactionType { get; set; }

    [JsonPropertyName("TNFTDT")]
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("TNFTTM")]
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string? TransactionTime { get; set; }

    [JsonPropertyName("TNFCLK")]
    [StringLength(5, MinimumLength = 5)]
    public string? Clerk { get; set; }

    [JsonPropertyName("TNFREG")]
    [StringLength(3, MinimumLength = 3)]
    public string? RegisterID { get; set; }

    [JsonPropertyName("TNFTTX")]
    [StringLength(5, MinimumLength = 5)]
    public string? TransactionNumber { get; set; }

    [JsonPropertyName("TNFTSQ")]
    [StringLength(5, MinimumLength = 5)]
    public string? TransactionSeq { get; set; }

    [JsonPropertyName("TNFFCD")]
    [StringLength(2, MinimumLength = 2)]
    public string? FundCode { get; set; }

    [JsonPropertyName("TNFAMT")]
    [StringLength(11, MinimumLength = 11)]
    public string? Amount { get; set; }

    [JsonPropertyName("TNFAMN")]
    [StringLength(1, MinimumLength = 1)]
    public string? AmountNegativeSign { get; set; }

    [JsonPropertyName("TNFCCD")]
    [StringLength(19, MinimumLength = 19)]
    public string? CreditCardNumber { get; set; }

    [JsonPropertyName("TNFEXP")]
    [StringLength(4, MinimumLength = 4)]
    public string? CardExpirationDate { get; set; }

    [JsonPropertyName("TNFAUT")]
    [StringLength(6, MinimumLength = 6)]
    public string? AuthNumber { get; set; }

    [JsonPropertyName("TNFRFC")]
    [StringLength(1, MinimumLength = 1)]
    public string? ReferenceCode { get; set; }

    [JsonPropertyName("TNFRDS")]
    [StringLength(16, MinimumLength = 16)]
    public string? ReferenceDesc { get; set; }

    [JsonPropertyName("TNFMBR")]
    [StringLength(8, MinimumLength = 8)]
    public string? CustomerMember { get; set; }

    [JsonPropertyName("TNFSTS")]
    [StringLength(1, MinimumLength = 1)]
    public string? Status { get; set; }

    [JsonPropertyName("TNFESI")]
    [StringLength(5, MinimumLength = 5)]
    public string? EmployeeSaleId { get; set; }

    [JsonPropertyName("TNFZIP")]
    [StringLength(10, MinimumLength = 10)]
    public string? PostalCode { get; set; }

    [JsonPropertyName("TNFMSR")]
    [StringLength(1, MinimumLength = 1)]
    public string? MagStripeFlag { get; set; }

    [JsonPropertyName("TNFEML")]
    [StringLength(60, MinimumLength = 60)]
    public string? EReceiptEmail { get; set; }

    [JsonPropertyName("TNFHSH")]
    [StringLength(64, MinimumLength = 64)]
    public string? PaymentHashValue { get; set; }

    [JsonPropertyName("TNFPLC")]
    [Range(0, 99999)]
    public int? PolledStore { get; set; }

    [JsonPropertyName("TNFPCN")]
    [Range(0, 9)]
    public int? PollCen { get; set; }

    [JsonPropertyName("TNFPDT")]
    [Range(0, 999999)]
    public int? PollDate { get; set; }

    [JsonPropertyName("TNFCCN")]
    [Range(0, 9)]
    public int? CreateCen { get; set; }

    [JsonPropertyName("TNFCDT")]
    [Range(0, 999999)]
    public int? CreateDate { get; set; }

    [JsonPropertyName("TNFCTM")]
    [Range(0, 999999)]
    public int? CreateTime { get; set; }

    [JsonPropertyName("ATFSST")]
    [Range(0, 99999)]
    public int? SalesStore { get; set; }

    [JsonPropertyName("ATFIST")]
    [Range(0, 99999)]
    public int? InvStore { get; set; }

    [JsonPropertyName("ATFOTX")]
    [StringLength(5, MinimumLength = 5)]
    public string? OriginalTransNumber { get; set; }

    [JsonPropertyName("ATFMBT")]
    [StringLength(1, MinimumLength = 1)]
    public string? CustomerType { get; set; }

    [JsonPropertyName("ATFORD")]
    [StringLength(8, MinimumLength = 8)]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("ATFPRO")]
    [StringLength(5, MinimumLength = 5)]
    public string? ProjectNumber { get; set; }
}
