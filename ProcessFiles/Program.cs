// See https://aka.ms/new-console-template for more information
//Console.WriteLine("Hello, World!");
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSubRona
{
//    class Main1
//    {
//        public static void Main(string?[] args)
//       {
//            MainClass Main1 = new MainClass();
//            Main1.Main1(args);
//        }
//    }


// An OrderRecord is an object meant for direct translation to a SQL command against the SDISLF table in MMS
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
    }

    // A TenderRecord is an object meant for direct translation to a SQL command against the SDITNF table in MMS
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

    // A RecordSet is an OrderRecord and TenderRecord that refer to the same transaction
    public class RecordSet
    {
        [JsonPropertyName("SDISLF")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OrderRecord? OrderRecord { get; set; }

        [JsonPropertyName("SDITNF")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TenderRecord? TenderRecord { get; set; }
    }


public class RetailEvent
    {
        [JsonPropertyName("businessContext")]
        public BusinessContext BusinessContext { get; set; }

        [JsonPropertyName("eventCategory")]
        public string? EventCategory { get; set; }

        [JsonPropertyName("eventId")]
        public string? EventId { get; set; }

        [JsonPropertyName("eventSubType")]
        public string? EventSubType { get; set; }

        [JsonPropertyName("eventType")]
        public string? EventType { get; set; }

        [JsonPropertyName("ingestedAt")]
        public DateTime IngestedAt { get; set; }

        [JsonPropertyName("messageType")]
        public string? MessageType { get; set; }

        [JsonPropertyName("occurredAt")]
        public DateTime OccurredAt { get; set; }

        [JsonPropertyName("references")]
        public References References { get; set; }

        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; set; }

        [JsonPropertyName("transaction")]
        public Transaction Transaction { get; set; }
    }

    public class BusinessContext
    {
        [JsonPropertyName("businessDay")]
        public DateTime BusinessDay { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("fulfillment")]
        public string? Fulfillment { get; set; }

        [JsonPropertyName("store")]
        public Store Store { get; set; }

        [JsonPropertyName("workstation")]
        public Workstation Workstation { get; set; }
    }

    public class Store
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("storeId")]
        public string? StoreId { get; set; }

        [JsonPropertyName("taxArea")]
        public string? TaxArea { get; set; }
    }

    public class Workstation
    {
        [JsonPropertyName("registerId")]
        public string? RegisterId { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public class References
    {
        [JsonPropertyName("sourceTransactionId")]
        public string? SourceTransactionId { get; set; }
    }

    public class Transaction
    {
        [JsonPropertyName("items")]
        public List<TransactionItem> Items { get; set; }

        [JsonPropertyName("totals")]
        public Totals Totals { get; set; }

        [JsonPropertyName("transactionType")]
        public string? TransactionType { get; set; }
    }

    public class TransactionItem
    {
        [JsonPropertyName("item")]
        public Item Item { get; set; }

        [JsonPropertyName("lineId")]
        public string? LineId { get; set; }

        [JsonPropertyName("pricing")]
        public Pricing Pricing { get; set; }

        [JsonPropertyName("quantity")]
        public Quantity Quantity { get; set; }
    }

    public class Item
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("sku")]
        public string? Sku { get; set; }
    }

    public class Pricing
    {
        [JsonPropertyName("extendedPrice")]
        public CurrencyAmount ExtendedPrice { get; set; }

        [JsonPropertyName("originalUnitPrice")]
        public CurrencyAmount OriginalUnitPrice { get; set; }

        [JsonPropertyName("unitPrice")]
        public CurrencyAmount UnitPrice { get; set; }
    }

    public class Quantity
    {
        [JsonPropertyName("uom")]
        public string? Uom { get; set; }

        [JsonPropertyName("value")]
        public int Value { get; set; }
    }

    public class Totals
    {
        [JsonPropertyName("discounts")]
        public CurrencyAmount Discounts { get; set; }

        [JsonPropertyName("gross")]
        public CurrencyAmount Gross { get; set; }

        [JsonPropertyName("net")]
        public CurrencyAmount Net { get; set; }

        [JsonPropertyName("tax")]
        public CurrencyAmount Tax { get; set; }
    }

    public class CurrencyAmount
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }  // Using string? to match JSON, or use decimal if preferred
    }

    class MainClass
    {
        RecordSet recordSet = new RecordSet();
        RetailEvent? retailEvent = null;

        public void Main1(string?[] args)
        {
            // Read the retail event
            retailEvent = ReadRecordSetFromFile(@"C:\TransactionTree\TTCustomers\Rona\PubSubProject\RonaPubSub\PubSubMessage_20251022154834.json");

            // Map to RecordSet
            recordSet = MapRetailEventToRecordSet(retailEvent);

            // Write to output file
            string outputPath = @"C:\TransactionTree\TTCustomers\Rona\PubSubProject\RonaPubSub\OutputRecordSet.json";
            WriteRecordSetToFile(recordSet, outputPath);
           
        }

        public RecordSet MapRetailEventToRecordSet(RetailEvent retailEvent)
        {
            var recordSet = new RecordSet
            {
                OrderRecord = new OrderRecord
                {
                    TransDate = retailEvent.BusinessContext.BusinessDay.ToString("yyyyMMdd"),
                    TransTime = retailEvent.OccurredAt.ToString("HHmmss"),
                    TransNumber = retailEvent.EventId,
                    RegisterID = retailEvent.BusinessContext.Workstation.RegisterId,
                    TransType = retailEvent.Transaction.TransactionType,
                    LineType = "ITEM",
                    TransSeq = "1"
                },
                TenderRecord = new TenderRecord
                {
                    TransactionDate = retailEvent.BusinessContext.BusinessDay.ToString("yyyyMMdd"),
                    TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"),
                    TransactionNumber = retailEvent.EventId,
                    RegisterID = retailEvent.BusinessContext.Workstation.RegisterId,
                    TransactionType = retailEvent.Transaction.TransactionType,
                    Amount = retailEvent.Transaction.Totals.Net.Value,
                    PolledStore = retailEvent.BusinessContext.Store.StoreId,
                    TransactionSeq = "1"
                }
            };

            // Map first item if exists
            if (retailEvent.Transaction.Items != null && retailEvent.Transaction.Items.Count > 0)
            {
                var firstItem = retailEvent.Transaction.Items[0];
                recordSet.OrderRecord.SKUNumber = firstItem.Item.Sku;
                recordSet.OrderRecord.OriginalPrice = firstItem.Pricing.OriginalUnitPrice.Value;
                recordSet.OrderRecord.Quantity = firstItem.Quantity.Value.ToString();

                // Try to parse storeId as int if possible
                if (int.TryParse(retailEvent.BusinessContext.Store.StoreId, out int storeId))
                {
                    recordSet.OrderRecord.PolledStore = storeId;
                }
            }

            return recordSet;
        }

        public static RetailEvent ReadRecordSetFromFile(string filePath)
        {
            try
            {
                string jsonContent = File.ReadAllText(filePath);
                var retailEvent = JsonSerializer.Deserialize<RetailEvent>(jsonContent);
                return retailEvent ?? new RetailEvent();
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"File not found: {ex.Message}");
                throw;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error: {ex.Message}");
                throw;
            }
        }

        public static void WriteRecordSetToFile(RecordSet recordSet, string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string jsonString = JsonSerializer.Serialize(recordSet, options);
                File.WriteAllText(filePath, jsonString);

                Console.WriteLine($"RecordSet written to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing file: {ex.Message}");
                throw;
            }
        }

        public static async Task WriteRecordSetToFileAsync(RecordSet recordSet, string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                using FileStream fileStream = File.Create(filePath);
                await JsonSerializer.SerializeAsync(fileStream, recordSet, options);

                Console.WriteLine($"RecordSet written to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing file: {ex.Message}");
                throw;
            }
        }
    }
}