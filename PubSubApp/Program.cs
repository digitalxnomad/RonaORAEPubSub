using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using PubSubApp;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

class Program
{
    static string Version = "PubSubApp 12/03/25 v1.0.1";

    static async Task Main()
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var pubSubConfig = new PubSubConfiguration();
        configuration.GetSection("PubSubConfiguration").Bind(pubSubConfig);

        string projectId = pubSubConfig.ProjectId;
        string topicId = pubSubConfig.TopicId;
        string subscriptionId = pubSubConfig.SubscriptionId;

        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        var subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);

        // Create publisher and subscriber directly
        var publisher = await PublisherClient.CreateAsync(topicName);
        var subscriber = await SubscriberClient.CreateAsync(subscriptionName);

        SimpleLogger.SetLogPath("c:\\opt\\transactiontree\\pubsub\\log\\pubsub.log", projectId);

        Console.WriteLine(Version);
        Console.WriteLine("✓ Publisher and Subscriber initialized\n");
        Console.WriteLine("ProjectId: " + projectId);
        Console.WriteLine("TopicId: " + topicId + "\n");
        Console.WriteLine("Listening for messages...\n");        
        SimpleLogger.LogInfo(Version);
        SimpleLogger.LogInfo("ProjectId: " + projectId);
        SimpleLogger.LogInfo("TopicId: " + topicId);    
        SimpleLogger.LogInfo("✓ Publisher and Subscriber initialized");
        SimpleLogger.LogInfo("Listening for messages...");

        // Start subscribing
        await subscriber.StartAsync(async (message, cancellationToken) =>
        {
            try
            {
                // Process the message
                string data = message.Data.ToStringUtf8();
                Console.WriteLine($"\n✓ Received: {message.MessageId}");
                SimpleLogger.LogInfo($"✓ Received: {message.MessageId}");
                Console.WriteLine($"Data: {data.Substring(0, Math.Min(50, data.Length))}...");
                SimpleLogger.LogInfo($"Data: {data}");

                MainClass mainClass = new MainClass();
                
                RetailEvent retailEvent = mainClass.ReadRecordSetFromString(data);
                
                // Map to RecordSet
                RecordSet recordSet = mainClass.MapRetailEventToRecordSet(retailEvent);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string jsonString = JsonSerializer.Serialize(recordSet, options);
                SimpleLogger.LogInfo($"✓ Published response: {jsonString}");
                // Publish response
                var responseMessage = new PubsubMessage
                {
                    Data = ByteString.CopyFromUtf8(jsonString)
                };
                responseMessage.Attributes.Add("responseFor", message.MessageId);

                string publishedId = await publisher.PublishAsync(jsonString);
                Console.WriteLine($"✓ Published response: {publishedId}");
                SimpleLogger.LogInfo($"✓ Published response: {publishedId}");

                return SubscriberClient.Reply.Ack;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                SimpleLogger.LogError($"✗ Error: {ex.Message}");
                return SubscriberClient.Reply.Nack;
            }
        });

        Console.WriteLine("Press Enter to stop...");
        Console.ReadLine();

        await subscriber.StopAsync(CancellationToken.None);
        await publisher.ShutdownAsync(TimeSpan.FromSeconds(15));
    }
}


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
        public BusinessContext? BusinessContext { get; set; }

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
        public References? References { get; set; }

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
        public int? SequenceNumber { get; set; }
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
        public List<TransactionItem> Items { get; set; }

        [JsonPropertyName("tenders")]
        public List<Tender>? Tenders { get; set; }

        [JsonPropertyName("totals")]
        public Totals Totals { get; set; }
    }

    public class Tender
    {
        [JsonPropertyName("tenderId")]
        public string? TenderId { get; set; }

        [JsonPropertyName("method")]
        public string? Method { get; set; }

        [JsonPropertyName("amount")]
        public CurrencyAmount? Amount { get; set; }
    }

    public class TransactionItem
    {
        [JsonPropertyName("item")]
        public Item? Item { get; set; }

        [JsonPropertyName("lineId")]
        public string? LineId { get; set; }

        [JsonPropertyName("pricing")]
        public Pricing? Pricing { get; set; }

        [JsonPropertyName("quantity")]
        public Quantity? Quantity { get; set; }
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
        public CurrencyAmount? ExtendedPrice { get; set; }

        [JsonPropertyName("originalUnitPrice")]
        public CurrencyAmount? OriginalUnitPrice { get; set; }

        [JsonPropertyName("unitPrice")]
        public CurrencyAmount? UnitPrice { get; set; }
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
            string? mappedTransactionTypeSLFTTP = retailEvent.Transaction?.TransactionType != null ? MapTransTypeSLFTTP(retailEvent.Transaction.TransactionType) : null;
            string? mappedTransactionTypeSLFLNT = retailEvent.Transaction?.TransactionType != null ? MapTransTypeSLFLNT(retailEvent.Transaction.TransactionType) : null;

            // Parse storeId as integer for PolledStore fields
            int? polledStoreInt = null;
            if (int.TryParse(retailEvent.BusinessContext?.Store?.StoreId, out int storeId))
            {
                polledStoreInt = storeId;
            }

            // Get current date/time for polling and creation timestamps
            DateTime now = DateTime.Now;
            int pollCen = GetCentury(now);
            int pollDate = GetDateAsInt(now);
            int createCen = pollCen;
            int createDate = pollDate;
            int createTime = GetTimeAsInt(now);

            var recordSet = new RecordSet
            {
                OrderRecord = new OrderRecord
                {
                    // Required Fields
                    TransType = mappedTransactionTypeSLFTTP,
                    LineType = mappedTransactionTypeSLFLNT,
                    TransDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                    TransTime = retailEvent.OccurredAt.ToString("HHmmss"),

                    // Transaction Identification
                    TransNumber = PadOrTruncate(retailEvent.EventId, 5),
                    TransSeq = "00001", // First sequence
                    RegisterID = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                    // Store Information
                    PolledStore = polledStoreInt,
                    PollCen = pollCen,
                    PollDate = pollDate,
                    CreateCen = createCen,
                    CreateDate = createDate,
                    CreateTime = createTime,

                    // Will be populated from first item below
                    SKUNumber = null,
                    Quantity = null,
                    OriginalPrice = null,

                    // Status - default to active
                    Status = "A"
                },
                TenderRecord = new TenderRecord
                {
                    // Required Fields
                    TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                    TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"),

                    // Transaction Identification
                    TransactionType = mappedTransactionTypeSLFTTP,
                    TransactionNumber = PadOrTruncate(retailEvent.EventId, 5),
                    TransactionSeq = "00001", // First sequence
                    RegisterID = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                    // Store Information
                    PolledStore = polledStoreInt,
                    PollCen = pollCen,
                    PollDate = pollDate,
                    CreateCen = createCen,
                    CreateDate = createDate,
                    CreateTime = createTime,

                    // Will be populated from first tender below
                    FundCode = null,
                    Amount = null,

                    // Status - default to active
                    Status = "A"
                }
            };

            // Map first item if exists
            if (retailEvent.Transaction?.Items != null && retailEvent.Transaction.Items.Count > 0)
            {
                var firstItem = retailEvent.Transaction.Items[0];

                // SKU Number - pad/truncate to 9 characters
                recordSet.OrderRecord.SKUNumber = PadOrTruncate(firstItem.Item?.Sku, 9);

                // Quantity - format decimal to 9 characters, handle negative for returns
                if (firstItem.Quantity != null)
                {
                    decimal qtyValue = firstItem.Quantity.Value;
                    bool isNegative = qtyValue < 0;
                    int qtyCents = (int)(Math.Abs(qtyValue) * 100);

                    recordSet.OrderRecord.Quantity = qtyCents.ToString().PadLeft(9, '0');
                    recordSet.OrderRecord.QuantityNegativeSign = isNegative ? "-" : " ";
                }

                // Original Price
                if (firstItem.Pricing?.OriginalUnitPrice?.Value != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(firstItem.Pricing.OriginalUnitPrice.Value, 9);
                    recordSet.OrderRecord.OriginalPrice = amount;
                    recordSet.OrderRecord.OriginalPriceNegativeSign = sign;
                }

                // Sell Price (unit price after discounts)
                if (firstItem.Pricing?.UnitPrice?.Value != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(firstItem.Pricing.UnitPrice.Value, 9);
                    recordSet.OrderRecord.ItemSellPrice = amount;
                    recordSet.OrderRecord.SellPriceNegativeSign = sign;
                }

                // Extended Value (total line amount)
                if (firstItem.Pricing?.ExtendedPrice?.Value != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(firstItem.Pricing.ExtendedPrice.Value, 11);
                    recordSet.OrderRecord.ExtendedValue = amount;
                    recordSet.OrderRecord.ExtendedValueNegativeSign = sign;
                }

                // Calculate discount if unit price differs from original
                if (firstItem.Pricing?.OriginalUnitPrice?.Value != null &&
                    firstItem.Pricing?.UnitPrice?.Value != null &&
                    decimal.TryParse(firstItem.Pricing.OriginalUnitPrice.Value, out decimal origPrice) &&
                    decimal.TryParse(firstItem.Pricing.UnitPrice.Value, out decimal unitPrice))
                {
                    decimal discountAmount = origPrice - unitPrice;
                    if (discountAmount != 0)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(discountAmount.ToString("F2"), 9);
                        recordSet.OrderRecord.DiscountAmount = amount;
                        recordSet.OrderRecord.DiscountAmountNegativeSign = sign;
                        recordSet.OrderRecord.DiscountType = discountAmount > 0 ? "01" : "00"; // 01=discount, 00=markup
                    }
                }

                // Item Scanned flag - assume scanned if UOM is "EA" (each), otherwise manual entry
                recordSet.OrderRecord.ItemScanned = firstItem.Quantity?.Uom == "EA" ? "Y" : "N";

                // Reference for line ID
                if (!string.IsNullOrEmpty(firstItem.LineId))
                {
                    recordSet.OrderRecord.ReferenceCode = "L";
                    recordSet.OrderRecord.ReferenceDesc = PadOrTruncate(firstItem.LineId, 16);
                }
            }

            // Map discount from totals if available
            if (retailEvent.Transaction?.Totals?.Discounts?.Value != null)
            {
                var (amount, sign) = FormatCurrencyWithSign(retailEvent.Transaction.Totals.Discounts.Value, 9);
                if (recordSet.OrderRecord.DiscountAmount == null)
                {
                    recordSet.OrderRecord.DiscountAmount = amount;
                    recordSet.OrderRecord.DiscountAmountNegativeSign = sign;
                }
            }

            // Map tax information if available
            if (retailEvent.Transaction?.Totals?.Tax?.Value != null &&
                decimal.TryParse(retailEvent.Transaction.Totals.Tax.Value, out decimal taxAmount) &&
                taxAmount > 0)
            {
                // Set tax flags - default to tax1 charged
                recordSet.OrderRecord.ChargedTax1 = "Y";
                recordSet.OrderRecord.ChargedTax2 = "N";
                recordSet.OrderRecord.ChargedTax3 = "N";
                recordSet.OrderRecord.ChargedTax4 = "N";

                // Tax authority and rate code from store's tax area
                if (!string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                {
                    recordSet.OrderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                    recordSet.OrderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                }
            }

            // Map reference transaction if this is a return or void
            if (retailEvent.References?.SourceTransactionId != null &&
                (retailEvent.Transaction?.TransactionType == "RETURN" ||
                 retailEvent.Transaction?.TransactionType == "VOID"))
            {
                // Parse source transaction ID to extract original transaction details
                string sourceId = retailEvent.References.SourceTransactionId;
                recordSet.OrderRecord.OriginalTxNumber = PadOrTruncate(sourceId, 5);
                recordSet.OrderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5);
                recordSet.OrderRecord.OriginalTxDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd");
                recordSet.OrderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3);
            }

            // Map first tender if exists
            if (retailEvent.Transaction?.Tenders != null && retailEvent.Transaction.Tenders.Count > 0)
            {
                var firstTender = retailEvent.Transaction.Tenders[0];

                // Map tender method to fund code
                recordSet.TenderRecord.FundCode = MapTenderMethodToFundCode(firstTender.Method);

                // Tender amount with sign
                if (firstTender.Amount?.Value != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(firstTender.Amount.Value, 11);
                    recordSet.TenderRecord.Amount = amount;
                    recordSet.TenderRecord.AmountNegativeSign = sign;
                }

                // Tender reference - use tender ID
                if (!string.IsNullOrEmpty(firstTender.TenderId))
                {
                    recordSet.TenderRecord.ReferenceCode = "T";
                    recordSet.TenderRecord.ReferenceDesc = PadOrTruncate(firstTender.TenderId, 16);
                }
            }
            else if (retailEvent.Transaction?.Totals?.Net?.Value != null)
            {
                // Fallback to net total if no tenders array
                var (amount, sign) = FormatCurrencyWithSign(retailEvent.Transaction.Totals.Net.Value, 11);
                recordSet.TenderRecord.Amount = amount;
                recordSet.TenderRecord.AmountNegativeSign = sign;
                recordSet.TenderRecord.FundCode = "01"; // Default cash
            }

            return recordSet;
        }

        // Helper method to format currency values to fixed-length strings
        private string FormatCurrency(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value)) return new string('0', length);

            // Parse and convert to cents (remove decimal point)
            if (decimal.TryParse(value, out decimal amount))
            {
                int cents = (int)(amount * 100);
                return cents.ToString().PadLeft(length, '0');
            }

            return new string('0', length);
        }

        // Helper method to format currency with separate sign field
        private (string amount, string sign) FormatCurrencyWithSign(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
            {
                return (new string('0', length), " ");
            }

            if (decimal.TryParse(value, out decimal amount))
            {
                bool isNegative = amount < 0;
                int cents = (int)(Math.Abs(amount) * 100);
                string formattedAmount = cents.ToString().PadLeft(length, '0');
                string sign = isNegative ? "-" : " ";
                return (formattedAmount, sign);
            }

            return (new string('0', length), " ");
        }

        // Helper method to pad or truncate string to exact length
        private string? PadOrTruncate(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new string(' ', length);
            }

            if (value.Length > length)
            {
                return value.Substring(0, length);
            }

            return value.PadLeft(length, '0');
        }

        // Get century digit (0-9) from date
        private int GetCentury(DateTime date)
        {
            int year = date.Year;
            return (year / 100) % 10; // Returns last digit of century (20 -> 0, 21 -> 1)
        }

        // Get date as integer in YYMMDD format
        private int GetDateAsInt(DateTime date)
        {
            return int.Parse(date.ToString("yyMMdd"));
        }

        // Get time as integer in HHMMSS format
        private int GetTimeAsInt(DateTime date)
        {
            return int.Parse(date.ToString("HHmmss"));
        }

        // Map tender method to fund code
        private string MapTenderMethodToFundCode(string? method)
        {
            return method?.ToUpper() switch
            {
                "CASH" => "01",
                "CREDIT" or "CREDIT_CARD" => "02",
                "DEBIT" or "DEBIT_CARD" => "03",
                "CHECK" => "04",
                "GIFT_CARD" => "05",
                _ => "01" // Default to cash
            };
        }
    
        private string MapTransTypeSLFTTP(string input)
        {
            return input switch
            {
                "SALE" => "01",
                "RETURN" => "04",
                "VOID" => "11",
                "OPEN" => "87",
                "CLOSE" => "88",
                _ => "Unknown: " + input
            };
        }

        private string MapTransTypeSLFLNT(string input)
        {
            return input switch
            {
                "SALE" => "01",
                "RETURN" => "02",
                "AR_PAYMENT" => "43",
                "LAYAWAY_PAYMENT" => "45",
                "LAYAWAY_SALE" => "50",
                "LAYAWAY_PICKUP" => "51",
                "LAYAWAY_DELETE" => "52",
                "LAYAWAY_FORFEIT" => "53",
                "SPECIAL_ORDER" => "69",
                "NO_SALE" => "98",
                "PAID_OUT" => "90",
                _ => "01" // Default to regular sale
            };
    }


    public RetailEvent ReadRecordSetFromString(string datain)
    {
        try
        {
            string jsonContent = datain;
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
