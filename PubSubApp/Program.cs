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
using System.Xml.Linq;

public partial class Program
{
    static string Version = "PubSubApp 12/29/25 v1.0.5";

    public static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var pubSubConfig = new PubSubConfiguration();
        configuration.GetSection("PubSubConfiguration").Bind(pubSubConfig);

        // Check for test mode
        if (args.Length > 0 && args[0] == "--test")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run --test <json-file-path>");
                return;
            }

            string jsonPath = args[1];
            await TestJsonFile(jsonPath);
            return;
        }

  
        

        string projectId = pubSubConfig.ProjectId;
        string topicId = pubSubConfig.TopicId;
        string subscriptionId = pubSubConfig.SubscriptionId;

        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        var subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);

        // Create publisher and subscriber directly
        var publisher = await PublisherClient.CreateAsync(topicName);
        var subscriber = await SubscriberClient.CreateAsync(subscriptionName);

        SimpleLogger.SetLogPath(pubSubConfig.LogPath, projectId);

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
                SimpleLogger.LogInfo($"✓ Mapped to RecordSet: {jsonString}");

                // Publish response with attributes
                var responseMessage = new PubsubMessage
                {
                    Data = ByteString.CopyFromUtf8(jsonString)
                };

                // Forward all original message attributes
                foreach (var attr in message.Attributes)
                {
                    responseMessage.Attributes[attr.Key] = attr.Value;
                }

                // Add response-specific attributes
                responseMessage.Attributes["responseFor"] = message.MessageId;
                responseMessage.Attributes["transformedAt"] = DateTime.UtcNow.ToString("O");

                string publishedId = await publisher.PublishAsync(responseMessage);
                Console.WriteLine($"✓ Published response: {publishedId}");
                SimpleLogger.LogInfo($"✓ Published response: {publishedId} with {responseMessage.Attributes.Count} attributes");

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

    static async Task TestJsonFile(string jsonPath)
    {
        try
        {
            // Load configuration for logging
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var pubSubConfig = new PubSubConfiguration();
            configuration.GetSection("PubSubConfiguration").Bind(pubSubConfig);

            // Initialize logger
            SimpleLogger.SetLogPath(pubSubConfig.LogPath, pubSubConfig.ProjectId);

            Console.WriteLine($"\n=== Test Mode ===");
            SimpleLogger.LogInfo("=== Test Mode ===");
            Console.WriteLine($"Reading: {jsonPath}\n");
            SimpleLogger.LogInfo($"Reading: {jsonPath}");

            if (!File.Exists(jsonPath))
            {
                Console.WriteLine($"✗ Error: File not found: {jsonPath}");
                SimpleLogger.LogError($"✗ Error: File not found: {jsonPath}");
                return;
            }

            string jsonContent = File.ReadAllText(jsonPath);
            Console.WriteLine($"✓ File read successfully ({jsonContent.Length} bytes)\n");
            SimpleLogger.LogInfo($"✓ File read successfully ({jsonContent.Length} bytes)");

            Console.WriteLine("=== Input JSON ===");
            Console.WriteLine(jsonContent);
            Console.WriteLine();
            SimpleLogger.LogInfo($"Input JSON: {jsonContent}");

            MainClass mainClass = new MainClass();

            Console.WriteLine("Parsing RetailEvent...");
            SimpleLogger.LogInfo("Parsing RetailEvent...");
            RetailEvent retailEvent = mainClass.ReadRecordSetFromString(jsonContent);
            Console.WriteLine($"✓ RetailEvent parsed successfully\n");
            SimpleLogger.LogInfo("✓ RetailEvent parsed successfully");

            Console.WriteLine("Mapping to RecordSet...");
            SimpleLogger.LogInfo("Mapping to RecordSet...");
            RecordSet recordSet = mainClass.MapRetailEventToRecordSet(retailEvent);
            Console.WriteLine($"✓ RecordSet mapped successfully\n");
            SimpleLogger.LogInfo("✓ RecordSet mapped successfully");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string jsonOutput = JsonSerializer.Serialize(recordSet, options);

            Console.WriteLine("=== Output JSON ===");
            Console.WriteLine(jsonOutput);
            Console.WriteLine();
            SimpleLogger.LogInfo($"Output JSON: {jsonOutput}");

            Console.WriteLine("=== Validation ===");
            SimpleLogger.LogInfo("=== Validation ===");
            ValidateRecordSet(recordSet);

            // Write output to file
            string outputPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", "output_" + Path.GetFileName(jsonPath));
            File.WriteAllText(outputPath, jsonOutput);
            Console.WriteLine($"\n✓ Output written to: {outputPath}");
            SimpleLogger.LogInfo($"✓ Output written to: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            SimpleLogger.LogError($"✗ Error: {ex.Message}");
            SimpleLogger.LogError($"Stack trace: {ex.StackTrace}");
        }
    }

    static void ValidateRecordSet(RecordSet recordSet)
    {
        if (recordSet == null)
        {
            Console.WriteLine("✗ RecordSet is null");
            return;
        }

        // Validate OrderRecords
        if (recordSet.OrderRecords != null && recordSet.OrderRecords.Count > 0)
        {
            Console.WriteLine($"✓ Found {recordSet.OrderRecords.Count} OrderRecord(s) in RIMSLF");

            for (int i = 0; i < recordSet.OrderRecords.Count; i++)
            {
                var orderRecord = recordSet.OrderRecords[i];
                Console.WriteLine($"\n  OrderRecord #{i + 1}:");

                // Required fields
                if (string.IsNullOrEmpty(orderRecord.TransType))
                    Console.WriteLine($"    ✗ TransType (SLFTTP) is required");
                else
                    Console.WriteLine($"    ✓ TransType: {orderRecord.TransType}");

                if (string.IsNullOrEmpty(orderRecord.LineType))
                    Console.WriteLine($"    ✗ LineType (SLFLNT) is required");
                else
                    Console.WriteLine($"    ✓ LineType: {orderRecord.LineType}");

                if (string.IsNullOrEmpty(orderRecord.TransDate))
                    Console.WriteLine($"    ✗ TransDate (SLFTDT) is required");
                else
                    Console.WriteLine($"    ✓ TransDate: {orderRecord.TransDate} (length: {orderRecord.TransDate.Length})");

                if (string.IsNullOrEmpty(orderRecord.TransTime))
                    Console.WriteLine($"    ✗ TransTime (SLFTTM) is required");
                else
                    Console.WriteLine($"    ✓ TransTime: {orderRecord.TransTime} (length: {orderRecord.TransTime.Length})");

                if (!string.IsNullOrEmpty(orderRecord.SKUNumber))
                    Console.WriteLine($"    ✓ SKU: {orderRecord.SKUNumber}");

                if (!string.IsNullOrEmpty(orderRecord.Quantity))
                    Console.WriteLine($"    ✓ Quantity: {orderRecord.Quantity} (sign: '{orderRecord.QuantityNegativeSign}')");

                if (!string.IsNullOrEmpty(orderRecord.ExtendedValue))
                    Console.WriteLine($"    ✓ ExtendedValue: {orderRecord.ExtendedValue} (sign: '{orderRecord.ExtendedValueNegativeSign}')");

                if (!string.IsNullOrEmpty(orderRecord.TransSeq))
                    Console.WriteLine($"    ✓ TransSeq: {orderRecord.TransSeq}");
            }
        }
        else
        {
            Console.WriteLine("⚠ No OrderRecords found in RIMSLF");
        }

        // Validate TenderRecords
        if (recordSet.TenderRecords != null && recordSet.TenderRecords.Count > 0)
        {
            Console.WriteLine($"\n✓ Found {recordSet.TenderRecords.Count} TenderRecord(s) in RIMTNF");

            for (int i = 0; i < recordSet.TenderRecords.Count; i++)
            {
                var tenderRecord = recordSet.TenderRecords[i];
                Console.WriteLine($"\n  TenderRecord #{i + 1}:");

                // Required fields
                if (string.IsNullOrEmpty(tenderRecord.TransactionDate))
                    Console.WriteLine($"    ✗ TransactionDate (TNFTDT) is required");
                else
                    Console.WriteLine($"    ✓ TransactionDate: {tenderRecord.TransactionDate} (length: {tenderRecord.TransactionDate.Length})");

                if (string.IsNullOrEmpty(tenderRecord.TransactionTime))
                    Console.WriteLine($"    ✗ TransactionTime (TNFTTM) is required");
                else
                    Console.WriteLine($"    ✓ TransactionTime: {tenderRecord.TransactionTime} (length: {tenderRecord.TransactionTime.Length})");

                if (!string.IsNullOrEmpty(tenderRecord.FundCode))
                    Console.WriteLine($"    ✓ FundCode: {tenderRecord.FundCode}");

                if (!string.IsNullOrEmpty(tenderRecord.Amount))
                    Console.WriteLine($"    ✓ Amount: {tenderRecord.Amount} (sign: '{tenderRecord.AmountNegativeSign}')");

                if (!string.IsNullOrEmpty(tenderRecord.TransactionSeq))
                    Console.WriteLine($"    ✓ TransactionSeq: {tenderRecord.TransactionSeq}");
            }
        }
        else
        {
            Console.WriteLine("\n⚠ No TenderRecords found in RIMTNF");
        }
    }
}


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
    }

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

    // A RecordSet contains multiple OrderRecords and TenderRecords for a transaction
    public class RecordSet
    {
        [JsonPropertyName("RIMSLF")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OrderRecord>? OrderRecords { get; set; }

        [JsonPropertyName("RIMTNF")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TenderRecord>? TenderRecords { get; set; }
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
                OrderRecords = new List<OrderRecord>(),
                TenderRecords = new List<TenderRecord>()
            };

            // Map ALL items (not just first one) - create one OrderRecord per item
            if (retailEvent.Transaction?.Items != null && retailEvent.Transaction.Items.Count > 0)
            {
                int sequence = 1;
                foreach (var item in retailEvent.Transaction.Items)
                {
                    var orderRecord = new OrderRecord
                    {
                        // Required Fields - per CSV specs
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = mappedTransactionTypeSLFLNT,
                        TransDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                        TransTime = retailEvent.OccurredAt.ToString("HHmmss"),

                        // Transaction Identification - with proper padding
                        TransNumber = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = sequence.ToString().PadLeft(5, '0'), // Increment for each item
                        RegisterID = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                        // Store Information
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,

                        // Status - default to active
                        Status = ""
                    };

                    // SKU Number - 9-digits with leading zeros
                    orderRecord.SKUNumber = PadOrTruncate(item.Item?.Sku, 9);

                    // Quantity - 9-digits without decimal (multiply by 100)
                    if (item.Quantity != null)
                    {
                        decimal qtyValue = item.Quantity.Value;
                        bool isNegative = qtyValue < 0;
                        int qtyCents = (int)(Math.Abs(qtyValue) * 100);

                        orderRecord.Quantity = qtyCents.ToString().PadLeft(9, '0');
                        orderRecord.QuantityNegativeSign = isNegative ? "-" : " ";
                    }

                    // Original Price - 9-digits without decimal
                    if (item.Pricing?.OriginalUnitPrice?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(item.Pricing.OriginalUnitPrice.Value, 9);
                        orderRecord.OriginalPrice = amount;
                        orderRecord.OriginalPriceNegativeSign = sign;
                        orderRecord.OriginalRetail = amount; // SLFORT same as SLFORG
                        orderRecord.OriginalRetailNegativeSign = sign;
                    }

                    // Sell Price (unit price after discounts) - 9-digits without decimal
                    if (item.Pricing?.UnitPrice?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(item.Pricing.UnitPrice.Value, 9);
                        orderRecord.ItemSellPrice = amount;
                        orderRecord.SellPriceNegativeSign = sign;
                    }

                    // Extended Value (Quantity * Net Price) - 11-digits without decimal
                    if (item.Pricing?.ExtendedPrice?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(item.Pricing.ExtendedPrice.Value, 11);
                        orderRecord.ExtendedValue = amount;
                        orderRecord.ExtendedValueNegativeSign = sign;
                    }

                    // Calculate discount if unit price differs from original
                    if (item.Pricing?.OriginalUnitPrice?.Value != null &&
                        item.Pricing?.UnitPrice?.Value != null &&
                        decimal.TryParse(item.Pricing.OriginalUnitPrice.Value, out decimal origPrice) &&
                        decimal.TryParse(item.Pricing.UnitPrice.Value, out decimal unitPrice))
                    {
                        decimal discountAmount = origPrice - unitPrice;
                        if (discountAmount != 0)
                        {
                            var (amount, sign) = FormatCurrencyWithSign(discountAmount.ToString("F2"), 9);
                            orderRecord.DiscountAmount = amount;
                            orderRecord.DiscountAmountNegativeSign = sign;
                            orderRecord.DiscountType = discountAmount > 0 ? "01" : "00";
                        }
                    }

                    // Item Scanned Y/N
                    orderRecord.ItemScanned = item.Quantity?.Uom == "EA" ? "Y" : "N";

                    // Tax flags - default to N
                    orderRecord.ChargedTax1 = "N";
                    orderRecord.ChargedTax2 = "N";
                    orderRecord.ChargedTax3 = "N";
                    orderRecord.ChargedTax4 = "N";

                    // Set tax flags if tax amount exists
                    if (retailEvent.Transaction?.Totals?.Tax?.Value != null &&
                        decimal.TryParse(retailEvent.Transaction.Totals.Tax.Value, out decimal taxAmount) &&
                        taxAmount > 0)
                    {
                        orderRecord.ChargedTax2 = "Y"; // Default to GST (Tax2)

                        // Tax authority and rate code from store's tax area
                        if (!string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                        {
                            orderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                            orderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                        }
                    }

                    // Reference for line ID
                    if (!string.IsNullOrEmpty(item.LineId))
                    {
                        orderRecord.ReferenceCode = "L";
                        orderRecord.ReferenceDesc = PadOrTruncate(item.LineId, 16);
                    }

                    // Map reference transaction if this is a return or void
                    if (retailEvent.References?.SourceTransactionId != null &&
                        (retailEvent.Transaction?.TransactionType == "RETURN" ||
                         retailEvent.Transaction?.TransactionType == "VOID"))
                    {
                        string sourceId = retailEvent.References.SourceTransactionId;
                        orderRecord.OriginalTxNumber = PadOrTruncate(sourceId, 5);
                        orderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5);
                        orderRecord.OriginalTxDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd");
                        orderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3);
                    }

                    // === CSV-specified field mappings ===

                    // Customer fields - per CSV rules
                    orderRecord.CustomerName = ""; // SLFCNM - Always blank
                    orderRecord.CustomerNumber = ""; // SLFNUM - Default blank
                    orderRecord.ZipCode = ""; // SLFZIP - Default blank, will be set by EPP logic if needed

                    // Till/Clerk - SLFCLK (from till number)
                    orderRecord.Clerk = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 5);

                    // Employee fields - per CSV rules
                    orderRecord.EmployeeCardNumber = 0; // SLFECN - Set to zero

                    // UPC Code - SLFUPC (SKU UPC if scanned, otherwise blank)
                    orderRecord.UPCCode = ""; // Default blank, TODO: detect if scanned

                    // Email - SLFEML (for ereceipt)
                    orderRecord.EReceiptEmail = ""; // Default blank, TODO: populate if ereceipt scenario

                    // Reason codes - SLFRSN (return/price override/post voided - left justified)
                    orderRecord.ReasonCode = ""; // Default blank, TODO: populate based on transaction type

                    // Discount reasons - per CSV rules
                    orderRecord.GroupDiscReason = "00"; // SLFGDR - Always '00'
                    orderRecord.RegDiscReason = "00"; // SLFRDR - Default '00', TODO: set to 'I2' when price vehicle code = MAN

                    // Blank fields - per CSV rules
                    orderRecord.OrderNumber = ""; // SLFORD - Always blank
                    orderRecord.ProjectNumber = ""; // ASFPRO - Always blank
                    orderRecord.SalesStore = 0; // ASFSST - Always blank (0)
                    orderRecord.InvStore = 0; // ASFIST - Always blank (0)

                    // Add this OrderRecord to the list
                    recordSet.OrderRecords.Add(orderRecord);
                    sequence++;
                }
            }

            // Map ALL tenders (not just first one) - create one TenderRecord per tender
            if (retailEvent.Transaction?.Tenders != null && retailEvent.Transaction.Tenders.Count > 0)
            {
                int sequence = 1;
                foreach (var tender in retailEvent.Transaction.Tenders)
                {
                    var tenderRecord = new TenderRecord
                    {
                        // Required Fields - per CSV specs
                        TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                        TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"),

                        // Transaction Identification - with proper padding
                        TransactionType = mappedTransactionTypeSLFTTP,
                        TransactionNumber = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransactionSeq = sequence.ToString().PadLeft(5, '0'), // Increment for each tender
                        RegisterID = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                        // Store Information
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,

                        // Status - blank for active
                        Status = ""
                    };

                    // Map tender method to fund code
                    tenderRecord.FundCode = MapTenderMethodToFundCode(tender.Method);

                    // Tender amount with sign
                    if (tender.Amount?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(tender.Amount.Value, 11);
                        tenderRecord.Amount = amount;
                        tenderRecord.AmountNegativeSign = sign;
                    }

                    // Tender reference - use tender ID
                    if (!string.IsNullOrEmpty(tender.TenderId))
                    {
                        tenderRecord.ReferenceCode = "T";
                        tenderRecord.ReferenceDesc = PadOrTruncate(tender.TenderId, 16);
                    }

                    // === CSV-specified tender field mappings ===

                    // Card/Payment fields - per CSV rules
                    tenderRecord.CreditCardNumber = ""; // TNFCCD - TODO: populate masked card number
                    tenderRecord.CardExpirationDate = ""; // TNFESI - Default blank
                    tenderRecord.AuthNumber = ""; // TNFAUT - TODO: populate authorization number
                    tenderRecord.MagStripeFlag = ""; // TNFMSR - TODO: populate based on card processing type
                    tenderRecord.PaymentHashValue = ""; // TNFHSH - TODO: populate from bank

                    // Customer/Clerk fields - per CSV rules
                    tenderRecord.CustomerMember = ""; // TNFMBR - Default blank
                    tenderRecord.PostalCode = ""; // TNFZIP - Always blank
                    tenderRecord.Clerk = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 5); // TNFCLK - Till number

                    // Employee sale ID - TNFESI
                    // Set to '#####' for employee sales where TNFTTP = '04', otherwise blank
                    if (mappedTransactionTypeSLFTTP == "04")
                    {
                        tenderRecord.EmployeeSaleId = "#####";
                    }
                    else
                    {
                        tenderRecord.EmployeeSaleId = "";
                    }

                    // Email - TNFEML (for ereceipt)
                    tenderRecord.EReceiptEmail = ""; // Default blank, TODO: populate if ereceipt scenario

                    // Blank fields - per CSV rules
                    tenderRecord.SalesStore = 0; // ATFSST - Always blank
                    tenderRecord.InvStore = 0; // ATFIST - Always blank
                    tenderRecord.OriginalTransNumber = ""; // ATFOTX - Always blank
                    tenderRecord.CustomerType = ""; // ATFMBT - Always blank
                    tenderRecord.OrderNumber = ""; // ATFORD - Always blank
                    tenderRecord.ProjectNumber = ""; // ATFPRO - Always blank

                    // Add this TenderRecord to the list
                    recordSet.TenderRecords.Add(tenderRecord);
                    sequence++;
                }
            }
        else if (retailEvent.Transaction?.Totals?.Net?.Value != null)
        {
            // Fallback: create one tender record with net total if no tenders array
            var tenderRecord = new TenderRecord
            {
                TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"),
                TransactionType = mappedTransactionTypeSLFTTP,
                TransactionNumber = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                TransactionSeq = "00001",
                RegisterID = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                PolledStore = polledStoreInt,
                PollCen = pollCen,
                PollDate = pollDate,
                CreateCen = createCen,
                CreateDate = createDate,
                CreateTime = createTime,
                FundCode = "CA", // Default cash (TNFFCD)
                Status = ""
            };

            var (amount, sign) = FormatCurrencyWithSign(retailEvent.Transaction.Totals.Net.Value, 11);
            tenderRecord.Amount = amount;
            tenderRecord.AmountNegativeSign = sign;

            recordSet.TenderRecords.Add(tenderRecord);
        }
        return recordSet;
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

        // Map tender method to fund code (TNFFCD) - 2-letter alpha codes
        private string MapTenderMethodToFundCode(string? method)
        {
            return method?.ToUpper() switch
            {
                "CASH" => "CA",
                "CHECK" or "CHEQUE" => "CH",
                "DEBIT" or "DEBIT_CARD" => "DC",
                "CREDIT" or "CREDIT_CARD" => "VI", // TODO: Detect actual card type (VI/MA/AX)
                "VISA" => "VI",
                "MASTERCARD" or "MASTER_CARD" => "MA",
                "AMEX" or "AMERICAN_EXPRESS" => "AX",
                "GIFT_CARD" => "PG", // TODO: Distinguish between PC/PG/PP/PX based on transaction
                "COUPON" => "CP",
                "TRAVELLERS_CHEQUE" or "TRAVELERS_CHECK" => "TC",
                "US_CASH" => "US",
                "FLEXITI" => "FX",
                "WEB_SALE" => "PL",
                "PENNY_ROUNDING" => "PR",
                "CHANGE" => "ZZ",
                _ => "CA" // Default to cash
            };
        }

        // Helper method to format currency values to fixed-length strings
        private string FormatCurrency(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // Parse and convert to cents (remove decimal point)
            if (decimal.TryParse(value, out decimal amount))
            {
                int cents = (int)(amount * 100);
                return cents.ToString().PadLeft(length, '0');
            }

            return "";
        }

        // Helper method to format currency with separate sign field
        private (string amount, string sign) FormatCurrencyWithSign(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ("", "");
            }

            if (decimal.TryParse(value, out decimal amount))
            {
                bool isNegative = amount < 0;
                int cents = (int)(Math.Abs(amount) * 100);
                string formattedAmount = cents.ToString().PadLeft(length, '0');
                string sign = isNegative ? "-" : "";
                return (formattedAmount, sign);
            }

            return ("", "");
        }

        // Helper method to truncate string if exceeds max length
        private string? PadOrTruncate(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.Length > length)
            {
                return value.Substring(0, length);
            }

            return value;
        }

        // Get century digit (0-9) from date
        private int GetCentury(DateTime date)
        {
            int year = date.Year;
            return (year / 100) % 10; // Returns last digit of century (20 -> 0, 21 -> 1)
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
