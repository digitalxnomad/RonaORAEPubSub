 using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using PubSubApp;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Xml.Linq;

public partial class Program
{
    static string Version = "PubSubApp 01/22/26 v1.0.24";

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

                RetailEvent retailEvent;
                try
                {
                    retailEvent = mainClass.ReadRecordSetFromString(data);
                }
                catch (JsonException jsonEx)
                {
                    // Invalid JSON - log and ACK to prevent redelivery
                    string errorMessage = $"JSON parsing error: {jsonEx.Message}";
                    Console.WriteLine($"✗ {errorMessage}");
                    SimpleLogger.LogError(errorMessage);
                    Console.WriteLine($"✓ Message {message.MessageId} acknowledged (invalid JSON, will not be redelivered)");
                    SimpleLogger.LogInfo($"✓ Message {message.MessageId} acknowledged (invalid JSON, will not be redelivered)");
                    return SubscriberClient.Reply.Ack; // ACK invalid JSON to prevent redelivery
                }

                // Validate ORAE v2.0.0 compliance (if not disabled)
                if (!pubSubConfig.DisableOraeValidation)
                {
                    var validationErrors = MainClass.ValidateOraeCompliance(retailEvent);
                    if (validationErrors.Count > 0)
                    {
                        string errorMessage = $"ORAE validation failed with {validationErrors.Count} error(s):\n" +
                                            string.Join("\n", validationErrors);
                        Console.WriteLine($"✗ {errorMessage}");
                        SimpleLogger.LogError(errorMessage);
                        Console.WriteLine($"✓ Message {message.MessageId} acknowledged (ORAE validation failed, will not be redelivered)");
                        SimpleLogger.LogInfo($"✓ Message {message.MessageId} acknowledged (ORAE validation failed, will not be redelivered)");
                        return SubscriberClient.Reply.Ack; // ACK to prevent redelivery of invalid ORAE data
                    }
                    Console.WriteLine("✓ ORAE v2.0.0 validation passed");
                    SimpleLogger.LogInfo("✓ ORAE v2.0.0 validation passed");
                }
                else
                {
                    Console.WriteLine("⚠ ORAE validation skipped (disabled in configuration)");
                    SimpleLogger.LogWarning("⚠ ORAE validation skipped (disabled in configuration)");
                }

                // Map to RecordSet
                RecordSet recordSet = mainClass.MapRetailEventToRecordSet(retailEvent);

                // Validate output RecordSet
                var outputErrors = MainClass.ValidateRecordSetOutput(recordSet);
                if (outputErrors.Count > 0)
                {
                    string errorMessage = $"RecordSet validation failed with {outputErrors.Count} error(s):\n" +
                                        string.Join("\n", outputErrors);
                    Console.WriteLine($"✗ {errorMessage}");
                    SimpleLogger.LogError(errorMessage);
                    throw new Exception(errorMessage);
                }
                Console.WriteLine("✓ RecordSet output validation passed");
                SimpleLogger.LogInfo("✓ RecordSet output validation passed");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string jsonString = JsonSerializer.Serialize(recordSet, options);

                // Log summary instead of full JSON (can be very large)
                int orderCount = recordSet.OrderRecords?.Count ?? 0;
                int tenderCount = recordSet.TenderRecords?.Count ?? 0;
                SimpleLogger.LogInfo($"✓ Mapped to RecordSet: {orderCount} OrderRecords, {tenderCount} TenderRecords");

                // Save output RecordSet to file
                if (!string.IsNullOrEmpty(pubSubConfig.OutputSavePath))
                {
                    try
                    {
                        Directory.CreateDirectory(pubSubConfig.OutputSavePath);
                        string outputFilePath = Path.Combine(pubSubConfig.OutputSavePath,
                            $"RecordSet_{DateTime.Now:yyyyMMddHHmmss}_{message.MessageId}.json");
                        File.WriteAllText(outputFilePath, jsonString);
                        Console.WriteLine($"✓ Saved output to: {outputFilePath}");
                        SimpleLogger.LogInfo($"✓ Saved output to: {outputFilePath} ({jsonString.Length} bytes)");
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"✗ Failed to save output file: {ex.Message}";
                        Console.WriteLine(errorMsg);
                        SimpleLogger.LogError(errorMsg, ex);
                        // Don't throw - continue with publishing
                    }
                }
                else
                {
                    SimpleLogger.LogWarning("⚠ OutputSavePath not configured - output file not saved");
                }

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

    static Task TestJsonFile(string jsonPath)
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
                return Task.CompletedTask;
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

            // Validate ORAE v2.0.0 compliance (if not disabled)
            if (!pubSubConfig.DisableOraeValidation)
            {
                Console.WriteLine("Validating ORAE v2.0.0 compliance...");
                SimpleLogger.LogInfo("Validating ORAE v2.0.0 compliance...");
                var validationErrors = MainClass.ValidateOraeCompliance(retailEvent);
                if (validationErrors.Count > 0)
                {
                    string errorMessage = $"✗ ORAE validation failed with {validationErrors.Count} error(s):\n" +
                                        string.Join("\n  - ", validationErrors.Prepend(""));
                    Console.WriteLine(errorMessage);
                    SimpleLogger.LogError(errorMessage);
                    return Task.CompletedTask;
                }
                Console.WriteLine("✓ ORAE v2.0.0 validation passed\n");
                SimpleLogger.LogInfo("✓ ORAE v2.0.0 validation passed");
            }
            else
            {
                Console.WriteLine("⚠ ORAE validation skipped (disabled in configuration)\n");
                SimpleLogger.LogWarning("⚠ ORAE validation skipped (disabled in configuration)");
            }

            Console.WriteLine("Mapping to RecordSet...");
            SimpleLogger.LogInfo("Mapping to RecordSet...");
            RecordSet recordSet = mainClass.MapRetailEventToRecordSet(retailEvent);
            Console.WriteLine($"✓ RecordSet mapped successfully\n");
            SimpleLogger.LogInfo("✓ RecordSet mapped successfully");

            // Validate output RecordSet
            Console.WriteLine("Validating RecordSet output...");
            SimpleLogger.LogInfo("Validating RecordSet output...");
            var outputErrors = MainClass.ValidateRecordSetOutput(recordSet);
                        
            if (outputErrors.Count > 0)
            {
                string errorMessage = $"✗ RecordSet validation failed with {outputErrors.Count} error(s):\n" +
                                    string.Join("\n  - ", outputErrors.Prepend(""));
                Console.WriteLine(errorMessage);
                SimpleLogger.LogError(errorMessage);
                return Task.CompletedTask;
            }
            Console.WriteLine("✓ RecordSet output validation passed\n");
            SimpleLogger.LogInfo("✓ RecordSet output validation passed");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string jsonOutput = JsonSerializer.Serialize(recordSet, options);

            // Log summary instead of full JSON (can be very large)
            int orderCount = recordSet.OrderRecords?.Count ?? 0;
            int tenderCount = recordSet.TenderRecords?.Count ?? 0;
            SimpleLogger.LogInfo($"Output JSON generated: {orderCount} OrderRecords, {tenderCount} TenderRecords ({jsonOutput.Length} bytes)");

            Console.WriteLine("=== Output JSON ===");
            Console.WriteLine(jsonOutput);
            Console.WriteLine();

            Console.WriteLine("=== Validation ===");
            SimpleLogger.LogInfo("=== Validation ===");
            ValidateRecordSet(recordSet);

            // Write output to file using configured path if available
            if (!string.IsNullOrEmpty(pubSubConfig.OutputSavePath))
            {
                try
                {
                    Directory.CreateDirectory(pubSubConfig.OutputSavePath);
                    string outputPath = Path.Combine(pubSubConfig.OutputSavePath,
                        $"RecordSet_{DateTime.Now:yyyyMMddHHmmss}_test.json");
                    File.WriteAllText(outputPath, jsonOutput);
                    Console.WriteLine($"\n✓ Output written to: {outputPath}");
                    SimpleLogger.LogInfo($"✓ Output written to: {outputPath} ({jsonOutput.Length} bytes)");
                }
                catch (Exception ex)
                {
                    string errorMsg = $"✗ Failed to save output file: {ex.Message}";
                    Console.WriteLine(errorMsg);
                    SimpleLogger.LogError(errorMsg, ex);
                }
            }
            else
            {
                try
                {
                    // Fallback to local directory if not configured
                    string outputPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", "output_" + Path.GetFileName(jsonPath));
                    File.WriteAllText(outputPath, jsonOutput);
                    Console.WriteLine($"\n✓ Output written to: {outputPath}");
                    SimpleLogger.LogInfo($"✓ Output written to: {outputPath} ({jsonOutput.Length} bytes)");
                }
                catch (Exception ex)
                {
                    string errorMsg = $"✗ Failed to save output file: {ex.Message}";
                    Console.WriteLine(errorMsg);
                    SimpleLogger.LogError(errorMsg, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            SimpleLogger.LogError($"✗ Error: {ex.Message}");
            SimpleLogger.LogError($"Stack trace: {ex.StackTrace}");
        }

        return Task.CompletedTask;
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
        public required Transaction Transaction { get; set; }
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

        [JsonPropertyName("pricing")]
        public Pricing? Pricing { get; set; }

        [JsonPropertyName("quantity")]
        public Quantity? Quantity { get; set; }

        [JsonPropertyName("taxes")]
        public List<TaxDetail>? Taxes { get; set; }
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

        [JsonPropertyName("override")]
        public CurrencyAmount? Override { get; set; }

        [JsonPropertyName("priceVehicle")]
        public string? PriceVehicle { get; set; }
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

        [JsonPropertyName("amount")]
        public CurrencyAmount? TaxAmount { get; set; }

        [JsonPropertyName("taxableAmount")]
        public CurrencyAmount? TaxableAmount { get; set; }

        [JsonPropertyName("taxExempt")]
        public bool? TaxExempt { get; set; }

        [JsonPropertyName("exemptReason")]
        public string? ExemptReason { get; set; }
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
            // Check for employee discount (will set SLFPVC and affect SLFTTP/SLFLNT)
            bool hasEmployeeDiscount = HasEmployeeDiscount(retailEvent);

            // Check for gift card tender
            bool hasGiftCardTender = HasGiftCardTender(retailEvent);

            // Check for customer ID (currently always blank, but ready for future)
            bool hasCustomerId = !string.IsNullOrEmpty(GetCustomerId(retailEvent));

            string? mappedTransactionTypeSLFTTP = retailEvent.Transaction?.TransactionType != null ?
                MapTransTypeSLFTTP(retailEvent.Transaction.TransactionType, hasEmployeeDiscount) : null;
            string? mappedTransactionTypeSLFLNT = retailEvent.Transaction?.TransactionType != null ?
                MapTransTypeSLFLNT(retailEvent.Transaction.TransactionType, hasEmployeeDiscount, hasGiftCardTender, hasCustomerId) : null;

            // Parse storeId as integer for PolledStore fields
            int? polledStoreInt = null;
            if (int.TryParse(retailEvent.BusinessContext?.Store?.StoreId, out int storeId))
            {
                polledStoreInt = storeId;
            }

            // Apply timezone adjustment based on store region
            DateTime transactionDateTime = ApplyTimezoneAdjustment(retailEvent.OccurredAt, retailEvent.BusinessContext?.Store?.StoreId);

            // Get date/time values from adjusted transaction time
            int pollCen = 1;  // Always 1 per specification
            int pollDate = GetDateAsInt(transactionDateTime); // SLFPDT - Use adjusted transaction date
            int createCen = 1;  // Always 1 per specification
            int createDate = pollDate;
            int createTime = GetTimeAsInt(transactionDateTime);

            var recordSet = new RecordSet
            {
                OrderRecords = new List<OrderRecord>(),
                TenderRecords = new List<TenderRecord>()
            };

            // Temporary lists to group records by type
            List<OrderRecord> itemRecords = new List<OrderRecord>();
            List<OrderRecord> taxRecords = new List<OrderRecord>();

            // Map ALL items (not just first one) - create one OrderRecord per item
            if (retailEvent.Transaction?.Items != null && retailEvent.Transaction.Items.Count > 0)
            {
                foreach (var item in retailEvent.Transaction.Items)
                {
                    var orderRecord = new OrderRecord
                    {
                        // Required Fields - per CSV specs
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = mappedTransactionTypeSLFLNT,
                        TransDate = retailEvent.OccurredAt.ToString("yyMMdd"), // SLFTDT - Use raw OccurredAt date
                        TransTime = retailEvent.OccurredAt.ToString("HHmmss"), // SLFTTM - Use raw OccurredAt time

                        // Transaction Identification - with proper padding
                        TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = "00000", // Placeholder - will be updated after grouping
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                        // Store Information
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,

                        // Status - default to active (space for active)
                        Status = " "
                    };

                    // SKU Number - 9-digits with leading zeros
                    orderRecord.SKUNumber = PadNumeric(item.Item?.Sku, 9);

                    // Quantity - 9-digits without decimal (multiply by 100)
                    if (item.Quantity != null)
                    {
                        decimal qtyValue = item.Quantity.Value;
                        bool isNegative = qtyValue < 0;
                        int qtyCents = (int)(Math.Abs(qtyValue) * 100);

                        orderRecord.Quantity = qtyCents.ToString().PadLeft(9, '0');
                        // SLFQTN: "-" for Return, "" for all else
                        orderRecord.QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "";
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

                    // Sell Price (extended price) - 9-digits without decimal
                    if (item.Pricing?.ExtendedPrice?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(item.Pricing.ExtendedPrice.Value, 9);
                        orderRecord.ItemSellPrice = amount;
                        orderRecord.SellPriceNegativeSign = sign;
                    }

                    // Extended Value (Quantity * Net Price) - 11-digits without decimal
                    // Calculate: Quantity * Override (if exists), otherwise Quantity * UnitPrice
                    decimal extendedValue = 0;
                    if (item.Quantity?.Value != null)
                    {
                        decimal quantity = item.Quantity.Value;

                        // Use override price if it exists, otherwise use unit price
                        string? priceToUse = item.Pricing?.Override?.Value ?? item.Pricing?.UnitPrice?.Value;

                        if (priceToUse != null && decimal.TryParse(priceToUse, out decimal priceValue))
                        {
                            extendedValue = quantity * priceValue;
                        }
                    }

                    var (amountExt, signExt) = FormatCurrencyWithSign(extendedValue.ToString("F2"), 11);
                    orderRecord.ExtendedValue = amountExt;
                    orderRecord.ExtendedValueNegativeSign = signExt;

                    // Override Price - default to zeros if not present
                    orderRecord.OverridePrice = "000000000"; // SLFOVR - 9 zeros when no override
                    orderRecord.OverridePriceNegativeSign = ""; // SLFOVN - Empty string

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

                    // Price Vehicle Code (SLFPVC) and Reference (SLFREF) - parse from pricing.priceVehicle
                    // Format: "LEFT:RIGHT" where LEFT goes to SLFPVC (4 chars) and RIGHT goes to SLFREF (12 chars)
                    if (!string.IsNullOrEmpty(item.Pricing?.PriceVehicle))
                    {
                        string[] parts = item.Pricing.PriceVehicle.Split(':');
                        if (parts.Length == 2)
                        {
                            orderRecord.PriceVehicleCode = PadOrTruncate(parts[0], 4); // Left side to SLFPVC
                            orderRecord.PriceVehicleReference = PadOrTruncate(parts[1], 12); // Right side to SLFREF
                        }
                        else
                        {
                            // If format is incorrect, pad the whole value to SLFPVC
                            orderRecord.PriceVehicleCode = PadOrTruncate(item.Pricing.PriceVehicle, 4);
                            orderRecord.PriceVehicleReference = PadOrTruncate("", 12);
                        }
                    }
                    else
                    {
                        // Fallback: use "EMP" for employee discounts if priceVehicle not provided
                        orderRecord.PriceVehicleCode = hasEmployeeDiscount ? PadOrTruncate("EMP", 4) : PadOrTruncate("", 4);
                        orderRecord.PriceVehicleReference = PadOrTruncate("", 12);
                    }

                    // SLFADC - Additional Code: "####" when POS price != regular retail, "0000" when regular price
                    bool isRegularPrice = true;
                    if (item.Pricing?.OriginalUnitPrice?.Value != null &&
                        item.Pricing?.UnitPrice?.Value != null &&
                        decimal.TryParse(item.Pricing.OriginalUnitPrice.Value, out decimal origPriceAdc) &&
                        decimal.TryParse(item.Pricing.UnitPrice.Value, out decimal unitPriceAdc))
                    {
                        isRegularPrice = (unitPriceAdc == origPriceAdc);
                    }
                    orderRecord.AdCode = isRegularPrice ? "0000" : "####";

                    // SLFADP - Ad Price: "000000000" when PVCode is REG or MAN, otherwise unit price
                    string pvCode = orderRecord.PriceVehicleCode?.Trim() ?? "";
                    if (pvCode == "REG" || pvCode == "MAN")
                    {
                        orderRecord.AdPrice = "000000000"; // 9 zeros for REG or MAN
                        orderRecord.AdPriceNegativeSign = ""; // Empty sign
                    }
                    else
                    {
                        // Use unit price formatted as 9-digit currency
                        if (item.Pricing?.UnitPrice?.Value != null)
                        {
                            var (amount, sign) = FormatCurrencyWithSign(item.Pricing.UnitPrice.Value, 9);
                            orderRecord.AdPrice = amount;
                            orderRecord.AdPriceNegativeSign = sign;
                        }
                        else
                        {
                            orderRecord.AdPrice = "000000000";
                            orderRecord.AdPriceNegativeSign = "";
                        }
                    }

                    // Parse item-level taxes
                    ParseItemTaxes(item, orderRecord, retailEvent);

                    // Reference fields
                    orderRecord.ReferenceCode = ""; // SLFRFC - Always empty string
                    orderRecord.ReferenceDesc = ""; // SLFRFD - Always empty string

                    // SLFOTS, SLFOTD, SLFOTR, SLFOTT - Set based on transaction type
                    if (mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04") // SALE or Employee SALE
                    {
                        orderRecord.OriginalTxStore = "00000"; // 5 zeros for sales
                        orderRecord.OriginalTxDate = "000000"; // SLFOTD - 6 zeros for sales
                        orderRecord.OriginalTxRegister = "000"; // SLFOTR - 3 zeros for sales
                        orderRecord.OriginalTxNumber = "00000"; // SLFOTT - 5 zeros for sales
                    }
                    else if (mappedTransactionTypeSLFTTP == "11") // VOID
                    {
                        orderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5); // StoreId for voids
                        orderRecord.OriginalTxDate = retailEvent.OccurredAt.ToString("yyMMdd"); // SLFOTD - occurred date for voids
                        orderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3); // SLFOTR - register number for voids
                        orderRecord.OriginalTxNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5); // SLFOTT - transaction number for voids
                    }

                    // Map reference transaction if this is a return or void
                    if (retailEvent.References?.SourceTransactionId != null &&
                        (retailEvent.Transaction?.TransactionType == "RETURN" ||
                         retailEvent.Transaction?.TransactionType == "VOID"))
                    {
                        string sourceId = retailEvent.References.SourceTransactionId;
                        if (retailEvent.Transaction?.TransactionType == "RETURN")
                        {
                            orderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5); // StoreId for returns
                            orderRecord.OriginalTxDate = retailEvent.OccurredAt.ToString("yyMMdd"); // SLFOTD - occurred date for returns
                            orderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3); // SLFOTR - register number for returns
                            orderRecord.OriginalTxNumber = PadOrTruncate(sourceId, 5); // SLFOTT - source transaction ID for returns
                        }
                    }

                    // === CSV-specified field mappings ===

                    // Customer fields - per CSV rules
                    orderRecord.CustomerName = ""; // SLFCNM - Always blank
                    orderRecord.CustomerNumber = ""; // SLFNUM - Default blank

                    // SLFZIP - Leave empty to omit validation (per validate:"omitempty,max=10")
                    // Customer postal code data not available in ORAE Transaction structure
                    orderRecord.ZipCode = "";

                    // Till/Clerk - SLFCLK (from till number) - right justified with zeros
                    orderRecord.Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5);

                    // Employee fields - per CSV rules
                    orderRecord.EmployeeCardNumber = 0; // SLFECN - Set to zero

                    // UPC Code - SLFUPC (SKU UPC if scanned, otherwise all zeros)
                    orderRecord.UPCCode = "0000000000000"; // 13 zeros when not scanned

                    // Email - SLFEML (for ereceipt)
                    orderRecord.EReceiptEmail = ""; // Default blank, TODO: populate if ereceipt scenario

                    // Reason codes - SLFRSN (return/price override/post voided - left justified)
                    orderRecord.ReasonCode = ""; // Default blank, TODO: populate based on transaction type

                    // Tax exemption fields - SLFTE1, SLFTE2, SLFTEN - always empty
                    orderRecord.TaxExemptId1 = ""; // SLFTE1 - Always empty
                    orderRecord.TaxExemptId2 = ""; // SLFTE2 - Always empty
                    orderRecord.TaxExemptionName = ""; // SLFTEN - Always empty

                    // Required fields with fixed values - per validation spec
                    orderRecord.OriginalSalesperson = "00000"; // SLFOSP - Required, must be "00000"
                    orderRecord.OriginalStore = "00000"; // SLFOST - Required, must be "00000"
                    orderRecord.GroupDiscAmount = "000000000"; // SLFGDA - Required, must be "000000000"
                    orderRecord.GroupDiscSign = ""; // SLFGDS - Must be empty string
                    orderRecord.SalesPerson = "00000"; // SLFSPS - 5 zeros when blank

                    // Discount fields - default to required values when no discount applied
                    if (string.IsNullOrEmpty(orderRecord.DiscountAmount))
                    {
                        orderRecord.DiscountAmount = "000000000"; // SLFDSA - Must be "000000000" per validation
                        orderRecord.DiscountType = ""; // SLFDST - Must be empty string
                        orderRecord.DiscountAmountNegativeSign = ""; // SLFDSN - Must be empty string
                    }

                    // Discount reasons - per CSV rules
                    orderRecord.GroupDiscReason = "00"; // SLFGDR - Always '00'

                    // SLFRDR - Set to 'I2' when PVC = 'MAN', otherwise '00'
                    string pvCodeForRdr = orderRecord.PriceVehicleCode?.Trim() ?? "";
                    orderRecord.RegDiscReason = (pvCodeForRdr == "MAN") ? "I2" : "00";

                    // Blank fields - per CSV rules
                    orderRecord.OrderNumber = ""; // SLFORD - Always blank
                    orderRecord.ProjectNumber = ""; // ASFPRO - Always blank
                    orderRecord.SalesStore = 0; // ASFSST - Always blank (0)
                    orderRecord.InvStore = 0; // ASFIST - Always blank (0)

                    // Add this OrderRecord to the item records list
                    itemRecords.Add(orderRecord);

                    // Add tax line item for this specific item (LineType = "XH")
                    if (item.Taxes != null)
                    {
                        // Calculate total tax for this item
                        decimal itemTaxTotal = 0;
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxAmount?.Value != null && decimal.TryParse(tax.TaxAmount.Value, out decimal taxAmount))
                            {
                                itemTaxTotal += taxAmount;
                            }
                        }

                        // Only create tax line if there's a non-zero tax amount
                        if (itemTaxTotal != 0)
                        {
                            string taxAuthority = DetermineTaxAuthority(retailEvent, itemTaxTotal);

                            var taxRecord = new OrderRecord
                            {
                                // Same transaction identifiers as parent item
                                TransType = mappedTransactionTypeSLFTTP,
                                LineType = "XH", // Tax line type per spec
                                TransDate = retailEvent.OccurredAt.ToString("yyMMdd"), // SLFTDT - Use raw OccurredAt date
                                TransTime = retailEvent.OccurredAt.ToString("HHmmss"), // SLFTTM - Use raw OccurredAt time
                                TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                                TransSeq = "00000", // Placeholder - will be updated after grouping
                                RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                                // Store Information
                                PolledStore = polledStoreInt,
                                PollCen = pollCen,
                                PollDate = pollDate,
                                CreateCen = createCen,
                                CreateDate = createDate,
                                CreateTime = createTime,
                                Status = " ",

                                // Tax-specific fields - all tax flags set to N per spec
                                ChargedTax1 = "N",
                                ChargedTax2 = "N",
                                ChargedTax3 = "N",
                                ChargedTax4 = "N",
                                TaxAuthCode = PadOrTruncate(taxAuthority, 6),

                                // Tax amount in ExtendedValue and ItemSellPrice
                                ExtendedValue = FormatCurrency(itemTaxTotal.ToString("F2"), 11),
                                ExtendedValueNegativeSign = itemTaxTotal < 0 ? "-" : "",
                                ItemSellPrice = FormatCurrency(itemTaxTotal.ToString("F2"), 9),
                                SellPriceNegativeSign = itemTaxTotal < 0 ? "-" : "",

                                // Blank fields for tax line
                                SKUNumber = "000000000", // Placeholder SKU for tax line (9 zeros per validation)
                                Quantity = "000000100", // Tax quantity = 1.00 (1 * 100 in cents format)
                                QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "", // SLFQTN: "-" for Return, "" for all else
                                OriginalPrice = "000000000", // SLFORG - 9 zeros for tax record
                                OriginalPriceNegativeSign = "",
                                OverridePrice = "000000000", // SLFOVR - 9 zeros when no override
                                OverridePriceNegativeSign = "", // SLFOVN - Empty string
                                OriginalRetail = "000000000", // SLFORT - 9 zeros for tax record
                                OriginalRetailNegativeSign = "",
                                ReferenceCode = "", // SLFRFC - Always empty string
                                ReferenceDesc = "", // SLFRFD - Always empty string

                                // SLFOTS, SLFOTD, SLFOTR, SLFOTT - Set based on transaction type (same logic as regular items)
                                OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" ? "00000" :
                                                 (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "02" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                                OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" ? "000000" :
                                                (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "02" ? retailEvent.OccurredAt.ToString("yyMMdd") : null),
                                OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" ? "000" :
                                                    (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "02" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                                OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" ? "00000" :
                                                  (mappedTransactionTypeSLFTTP == "11" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                                  (mappedTransactionTypeSLFTTP == "02" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) : null)),

                                CustomerName = "",
                                CustomerNumber = "",
                                ZipCode = "",
                                Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                                EmployeeCardNumber = 0,
                                UPCCode = "0000000000000", // SLFUPC - 13 zeros
                                EReceiptEmail = "",
                                ReasonCode = "",
                                TaxExemptId1 = "", // SLFTE1 - Always empty
                                TaxExemptId2 = "", // SLFTE2 - Always empty
                                TaxExemptionName = "", // SLFTEN - Always empty
                                AdCode = "0000", // SLFADC - Always "0000" for tax records
                                AdPrice = "000000000", // SLFADP - Always "000000000" for tax records
                                AdPriceNegativeSign = "", // SLFADN - Empty string for tax records
                                PriceVehicleCode = "", // SLFPVC - Empty string for tax records
                                PriceVehicleReference = "", // SLFREF - Empty string for tax records

                                // Required fields with fixed values - per validation spec
                                OriginalSalesperson = "00000", // SLFOSP - Required
                                OriginalStore = "00000", // SLFOST - Required
                                GroupDiscAmount = "000000000", // SLFGDA - Required
                                GroupDiscSign = "", // SLFGDS - Empty string
                                SalesPerson = "00000", // SLFSPS - 5 zeros when blank
                                DiscountAmount = "000000000", // SLFDSA - Must be "000000000"
                                DiscountType = "", // SLFDST - Empty string
                                DiscountAmountNegativeSign = "", // SLFDSN - Empty string

                                GroupDiscReason = "00",
                                RegDiscReason = "00",
                                OrderNumber = "",
                                ProjectNumber = "",
                                SalesStore = 0,
                                InvStore = 0,
                                ItemScanned = "N"
                            };

                            taxRecords.Add(taxRecord);
                        }
                    }
                }

            }

            // Group records: Add all item records first, then all tax records
            // Update sequence numbers after grouping
            int sequence = 1;

            foreach (var itemRecord in itemRecords)
            {
                itemRecord.TransSeq = sequence.ToString().PadLeft(5, '0');
                recordSet.OrderRecords.Add(itemRecord);
                sequence++;
            }

            foreach (var taxRecord in taxRecords)
            {
                taxRecord.TransSeq = sequence.ToString().PadLeft(5, '0');
                recordSet.OrderRecords.Add(taxRecord);
                sequence++;
            }

            // Map ALL tenders (not just first one) - create one TenderRecord per tender
            // Continue sequence from OrderRecords (will be updated after grouping)
            if (retailEvent.Transaction?.Tenders != null && retailEvent.Transaction.Tenders.Count > 0)
            {
                foreach (var tender in retailEvent.Transaction.Tenders)
                {
                    var tenderRecord = new TenderRecord
                    {
                        // Required Fields - per CSV specs
                        TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                        TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"), // TNFTTM - Use raw OccurredAt time

                        // Transaction Identification - with proper padding
                        TransactionType = mappedTransactionTypeSLFTTP,
                        TransactionNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransactionSeq = "00000", // Placeholder - will be updated after grouping
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                        // Store Information
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,

                        // Status - space for active
                        Status = " "
                    };

                    // Map tender method to fund code
                    // Use card.scheme if available, otherwise use tender.method
                    if (tender.Card != null && !string.IsNullOrEmpty(tender.Card.Scheme))
                    {
                        tenderRecord.FundCode = MapCardSchemeToFundCode(tender.Card.Scheme);
                    }
                    else
                    {
                        tenderRecord.FundCode = MapTenderMethodToFundCode(tender.Method);
                    }

                    // Tender amount with sign
                    if (tender.Amount?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(tender.Amount.Value, 11);
                        tenderRecord.Amount = amount;
                        tenderRecord.AmountNegativeSign = sign;
                    }

                    // Tender reference - use tender ID
                    // TNFRDC - Always blank
                    tenderRecord.ReferenceCode = ""; // TNFRDC - Always blank

                    // TNFRDS should be blank for credit, debit, and flexiti tender types
                    string tenderMethod = tender.Method?.ToUpper() ?? "";
                    bool isBlankReferenceType = tenderMethod.Contains("CREDIT") ||
                                                tenderMethod.Contains("DEBIT") ||
                                                tenderMethod == "FLEXITI";

                    if (!string.IsNullOrEmpty(tender.TenderId))
                    {
                        tenderRecord.ReferenceDesc = isBlankReferenceType ? "" : PadOrTruncate(tender.TenderId, 16); // TNFRDS - Blank for credit/debit/flexiti
                    }
                    else
                    {
                        tenderRecord.ReferenceDesc = "";
                        tenderRecord.ReferenceCode = "";
                        tenderRecord.ReferenceDesc = PadOrTruncate(tender.TenderId, 16);
                    }

                    // === CSV-specified tender field mappings ===

                    // Card/Payment fields - populate from tender.card if available
                    if (tender.Card != null)
                    {
                        // TNFCCD - Credit card number: scheme + last4, padded left with * to 19 chars
                        string cardNumber = $"{tender.Card.Scheme ?? ""}{tender.Card.Last4 ?? ""}";
                        tenderRecord.CreditCardNumber = cardNumber.PadLeft(19, '*');

                        // TNFAUT - Authorization code, padded to 6 chars
                        tenderRecord.AuthNumber = PadOrTruncate(tender.Card.AuthCode ?? "", 6);

                        // TNFMSR - Mag stripe flag from card.emv.tags.magStrip, default to " " (1 space)
                        tenderRecord.MagStripeFlag = PadOrTruncate(tender.Card.Emv?.Tags?.MagStrip ?? " ", 1);
                    }
                    else
                    {
                        tenderRecord.CreditCardNumber = PadOrTruncate("", 19); // Empty when no card data
                        tenderRecord.AuthNumber = PadOrTruncate("", 6); // Empty when no card data
                        tenderRecord.MagStripeFlag = " "; // TNFMSR - Default to 1 space when no card data
                    }

                    tenderRecord.CardExpirationDate = "0000"; // TNFEXP - Must be "0000" per validation spec
                    tenderRecord.PaymentHashValue = ""; // TNFHSH - TODO: populate from bank

                    // Customer/Clerk fields - per CSV rules
                    tenderRecord.CustomerMember = ""; // TNFMBR - Default blank
                    tenderRecord.PostalCode = ""; // TNFZIP - Always blank
                    tenderRecord.Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5); // TNFCLK - Till number, right justified with zeros

                    // Employee sale ID - TNFESI
                    // Set to '#####' for employee sales where TNFTTP = '04', otherwise blank
                    if (mappedTransactionTypeSLFTTP == "04")
                    {
                        tenderRecord.EmployeeSaleId = "#####";
                    }
                    else
                    {
                        tenderRecord.EmployeeSaleId = ""; // Blank
                    }

                    // Email - TNFEML (for ereceipt)
                    tenderRecord.EReceiptEmail = ""; // Default blank, TODO: populate if ereceipt scenario

                    // Blank fields - per CSV rules
                    tenderRecord.SalesStore = 0; // ATFSST - Always blank
                    tenderRecord.InvStore = 0; // ATFIST - Always blank
                    tenderRecord.OriginalTransNumber = ""; // ATFOTX - Always blank
                    tenderRecord.CustomerType = " "; // ATFMBT (1 space) - Always blank
                    tenderRecord.OrderNumber = ""; // ATFORD - Always blank
                    tenderRecord.ProjectNumber = ""; // ATFPRO - Always blank

                    // Add this TenderRecord to the list (will update sequence after grouping)
                    recordSet.TenderRecords.Add(tenderRecord);
                }
            }
            else if (retailEvent.Transaction?.Totals?.Net?.Value != null)
            {
                // Fallback: create one tender record with net total if no tenders array
                var tenderRecord = new TenderRecord
                {
                    TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyMMdd"),
                    TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"), // TNFTTM - Use raw OccurredAt time
                    TransactionType = mappedTransactionTypeSLFTTP,
                    TransactionNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                    TransactionSeq = "00000", // Placeholder - will be updated after grouping
                    RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                    PolledStore = polledStoreInt,
                    PollCen = pollCen,
                    PollDate = pollDate,
                    CreateCen = createCen,
                    CreateDate = createDate,
                    CreateTime = createTime,
                    FundCode = "CA", // Default cash (TNFFCD)
                    Status = " " // Space for active
                };

                var (amount, sign) = FormatCurrencyWithSign(retailEvent.Transaction.Totals.Net.Value, 11);
                tenderRecord.Amount = amount;
                tenderRecord.AmountNegativeSign = sign;

                recordSet.TenderRecords.Add(tenderRecord);
            }

            // Update all tender record sequences (continuing from last OrderRecord sequence)
            foreach (var tenderRecord in recordSet.TenderRecords)
            {
                tenderRecord.TransactionSeq = sequence.ToString().PadLeft(5, '0');
                sequence++;
            }

            return recordSet;
     }

        // Apply timezone adjustment based on store region
        // Western region (BC, AB, SK, MB): subtract 8 hours
        // Eastern region (ON, QC): subtract 5 hours
        private DateTime ApplyTimezoneAdjustment(DateTime occurredAt, string? storeId)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return occurredAt.AddHours(-5); // Default to eastern
            }

            bool isWesternRegion = IsWesternRegionStore(storeId);
            int hoursOffset = isWesternRegion ? -8 : -5;
            return occurredAt.AddHours(hoursOffset);
        }

        // Check if transaction has employee discount
        // Employee discounts are indicated by specific price vehicle codes or discount patterns
        private bool HasEmployeeDiscount(RetailEvent retailEvent)
        {
            // Check if any item has a discount that indicates employee pricing
            // For now, we'll detect this by checking for specific discount patterns
            // This can be enhanced based on actual employee discount indicators in the data
            if (retailEvent.Transaction?.Items != null)
            {
                foreach (var item in retailEvent.Transaction.Items)
                {
                    // Check if item has pricing with discount that might indicate employee sale
                    // This is a placeholder - actual logic should be based on business rules
                    // Common indicators: specific SKU patterns, discount codes, price vehicle codes
                    if (item.Pricing?.OriginalUnitPrice?.Value != null &&
                        item.Pricing?.UnitPrice?.Value != null)
                    {
                        decimal originalPrice;
                        decimal unitPrice;
                        if (decimal.TryParse(item.Pricing.OriginalUnitPrice.Value, out originalPrice) &&
                            decimal.TryParse(item.Pricing.UnitPrice.Value, out unitPrice))
                        {
                            // If discount is greater than 30%, might be employee discount
                            // This threshold can be adjusted based on business rules
                            decimal discountPercent = originalPrice > 0 ? ((originalPrice - unitPrice) / originalPrice) * 100 : 0;
                            if (discountPercent >= 30)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        // Check if transaction has gift card tender
        private bool HasGiftCardTender(RetailEvent retailEvent)
        {
            if (retailEvent.Transaction?.Tenders != null)
            {
                foreach (var tender in retailEvent.Transaction.Tenders)
                {
                    if (tender.Method == "GIFT_CARD")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Get customer ID from transaction
        // Currently returns empty as Transaction doesn't have Customer property
        // Ready for future enhancement when customer data is available
        private string GetCustomerId(RetailEvent retailEvent)
        {
            // TODO: When customer data is added to Transaction, extract customer ID here
            // For now, return empty string
            return "";
        }

        // Determine if store is in western region (BC, AB, SK, MB) based on store ID
        private bool IsWesternRegionStore(string storeId)
        {
            if (!int.TryParse(storeId, out int storeNum))
            {
                return false;
            }

            // Western region stores (BC, AB, SK, MB) based on CSV data:
            // BC/AB/SK/MB stores: 170, 286, 384, 489, 103-104, 611-647, 61XXX, 62450, 63XXX, 64670, 65950
            // Plus specific 82XXX and 83XXX stores in western provinces

            // Single store IDs
            if (storeNum == 170 || storeNum == 286 || storeNum == 384 || storeNum == 489 ||
                storeNum == 103 || storeNum == 104)
            {
                return true;
            }

            // 6XX range (AB stores)
            if (storeNum >= 611 && storeNum <= 647)
            {
                return true;
            }

            // 61XXX range (BC stores)
            if (storeNum >= 61000 && storeNum <= 61999)
            {
                return true;
            }

            // 62450 (AB), 63XXX (SK), 64670 (MB), 65950 (BC)
            if (storeNum == 62450 || (storeNum >= 63000 && storeNum <= 63999) ||
                storeNum == 64670 || storeNum == 65950)
            {
                return true;
            }

            // Western 82XXX and 83XXX stores
            if (storeNum == 82952 || storeNum == 82953 || storeNum == 88007 ||
                storeNum == 83059 || storeNum == 83105 || storeNum == 83158 || storeNum == 83211 ||
                storeNum == 83230 || storeNum == 83309 || storeNum == 83313 || storeNum == 83318 ||
                storeNum == 83706 || storeNum == 83714 || storeNum == 83323 || storeNum == 83330 ||
                storeNum == 83702 || storeNum == 83704 || storeNum == 83285 || storeNum == 83718 ||
                storeNum == 83163 || storeNum == 83208)
            {
                return true;
            }

            // All other stores are eastern region (ON, QC)
            return false;
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
                "DEBIT" or "DEBIT_CARD" or "DEBITATM" => "DC",
                "CREDIT" or "CREDIT_CARD" => "VI", // Default to VISA if card scheme not available
                "VISA" => "VI",
                "MASTERCARD" or "MASTER_CARD" => "MA",
                "AMEX" or "AMERICAN_EXPRESS" => "AX",
                "GIFT_CARD" or "GIFTCARD" => "PG", // Gift card redeemed
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

        private string MapCardSchemeToFundCode(string? scheme)
        {
            return scheme?.ToUpper() switch
            {
                "VISA" => "VI",
                "MASTERCARD" or "MASTER_CARD" or "MC" => "MA",
                "AMEX" or "AMERICAN EXPRESS" or "AMERICANEXPRESS" => "AX",
                "DEBIT" or "DEBITATM" => "DC",
                "GIFTCARD" or "GIFT_CARD" => "PG",
                _ => "VI" // Default to VISA for unknown card schemes
            };
        }

        // Parse item-level taxes and map to OrderRecord tax fields
        private void ParseItemTaxes(TransactionItem item, OrderRecord orderRecord, RetailEvent retailEvent)
        {
            // Detect province/state for tax logic
            string? province = GetProvince(retailEvent);
            bool isOntario = province?.ToUpper() == "ON" || province?.ToUpper() == "ONTARIO";

            // Tax flags - default to N
            orderRecord.ChargedTax1 = "N";
            orderRecord.ChargedTax2 = "N";
            orderRecord.ChargedTax3 = "N";
            orderRecord.ChargedTax4 = "N";

            // Check if item has item-level taxes
            if (item.Taxes != null && item.Taxes.Count > 0)
            {
                // Process each tax detail
                foreach (var tax in item.Taxes)
                {
                    // Skip if tax is exempt
                    if (tax.TaxExempt == true)
                    {
                        continue;
                    }

                    // Check if tax amount is greater than zero
                    decimal taxAmount = 0;
                    if (tax.TaxAmount?.Value != null)
                    {
                        decimal.TryParse(tax.TaxAmount.Value, out taxAmount);
                    }

                    if (taxAmount <= 0)
                        continue;

                    // Map tax type/category to ChargedTax1-4 flags
                    string? taxType = tax.TaxType?.ToUpper() ?? tax.TaxCategory?.ToUpper();

                    // Ontario-specific logic: SLFTX2=N, SLFTX3=Y for HST
                    if (isOntario)
                    {
                        switch (taxType)
                        {
                            case "HST":
                            case "HARMONIZED":
                                // Ontario HST goes to Tax3
                                orderRecord.ChargedTax2 = "N"; // GST not separate
                                orderRecord.ChargedTax3 = "Y"; // HST
                                break;
                            case "GST":
                            case "FEDERAL":
                                // GST only (rare in Ontario)
                                orderRecord.ChargedTax2 = "N";
                                orderRecord.ChargedTax3 = "Y";
                                break;
                            case "PST":
                            case "PROVINCIAL":
                                // PST (not used in Ontario, but if present)
                                orderRecord.ChargedTax1 = "Y";
                                break;
                            case "MUNICIPAL":
                            case "LOCAL":
                            case "CITY":
                                orderRecord.ChargedTax4 = "Y";
                                break;
                            default:
                                // Default to HST for Ontario
                                orderRecord.ChargedTax2 = "N";
                                orderRecord.ChargedTax3 = "Y";
                                break;
                        }
                    }
                    else
                    {
                        // Non-Ontario provinces: use original logic
                        switch (taxType)
                        {
                            case "PST":
                            case "PROVINCIAL":
                            case "STATE":
                                orderRecord.ChargedTax1 = "Y";
                                break;
                            case "GST":
                            case "FEDERAL":
                            case "VAT":
                            case "NATIONAL":
                                orderRecord.ChargedTax2 = "Y";
                                break;
                            case "HST":
                            case "HARMONIZED":
                                // HST is combined PST+GST for non-Ontario
                                orderRecord.ChargedTax1 = "Y";
                                orderRecord.ChargedTax2 = "Y";
                                break;
                            case "QST":
                            case "QUEBEC":
                                orderRecord.ChargedTax3 = "Y";
                                break;
                            case "MUNICIPAL":
                            case "LOCAL":
                            case "CITY":
                                orderRecord.ChargedTax4 = "Y";
                                break;
                            default:
                                // Default unrecognized tax types to GST (Tax2)
                                orderRecord.ChargedTax2 = "Y";
                                break;
                        }
                    }

                    // Set tax authority code from first tax with authority
                    if (string.IsNullOrEmpty(orderRecord.TaxAuthCode) && !string.IsNullOrEmpty(tax.TaxAuthority))
                    {
                        orderRecord.TaxAuthCode = PadOrTruncate(tax.TaxAuthority, 6);
                    }

                    // Set tax rate code from first tax with code
                    if (string.IsNullOrEmpty(orderRecord.TaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                    {
                        orderRecord.TaxRateCode = PadOrTruncate(tax.TaxCode, 6);
                    }
                }

                // If no tax authority/rate codes found in item taxes, use store tax area
                if (string.IsNullOrEmpty(orderRecord.TaxAuthCode) &&
                    !string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                {
                    orderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                }

                if (string.IsNullOrEmpty(orderRecord.TaxRateCode) &&
                    !string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                {
                    orderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                }
            }
            else
            {
                // Fallback: Use transaction-level tax if no item-level taxes
                if (retailEvent.Transaction?.Totals?.Tax?.Value != null &&
                    decimal.TryParse(retailEvent.Transaction.Totals.Tax.Value, out decimal taxAmount) &&
                    taxAmount > 0)
                {
                    // Ontario: SLFTX2=N, SLFTX3=Y
                    if (isOntario)
                    {
                        orderRecord.ChargedTax2 = "N";
                        orderRecord.ChargedTax3 = "Y";
                    }
                    else
                    {
                        orderRecord.ChargedTax2 = "Y"; // Default to GST (Tax2)
                    }

                    // Tax authority and rate code from store's tax area
                    if (!string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                    {
                        orderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                        orderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                    }
                }
            }
        }

        // Helper method to format currency values to fixed-length strings
        private string FormatCurrency(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
                return ""; // Return empty string for blank currency fields

            // Parse and convert to cents (remove decimal point)
            if (decimal.TryParse(value, out decimal amount))
            {
                int cents = (int)(amount * 100);
                return cents.ToString().PadLeft(length, '0');
            }

            return ""; // Default to empty string if parse fails
        }

        // Helper method to format currency with separate sign field
        private (string amount, string sign) FormatCurrencyWithSign(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ("", ""); // Return empty string for amount and sign
            }

            if (decimal.TryParse(value, out decimal amount))
            {
                bool isNegative = amount < 0;
                int cents = (int)(Math.Abs(amount) * 100);
                string formattedAmount = cents.ToString().PadLeft(length, '0');
                string sign = isNegative ? "-" : ""; // Use empty string for positive/no sign
                return (formattedAmount, sign);
            }

            return ("", ""); // Default to empty strings
        }

        // Helper method to pad or truncate string to exact required length
        private string? PadOrTruncate(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ""; // Return empty string for blank fields
            }

            if (value.Length > length)
            {
                return value.Substring(0, length); // Truncate if too long
            }

            if (value.Length < length)
            {
                return value.PadRight(length, ' '); // Pad with spaces if too short
            }

            return value; // Exact length
        }

        // Helper method to pad numeric fields with leading zeros
        private string? PadNumeric(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ""; // Return empty string for blank numeric fields
            }

            if (value.Length > length)
            {
                return value.Substring(0, length); // Truncate if too long
            }

            if (value.Length < length)
            {
                return value.PadLeft(length, '0'); // Pad with leading zeros if too short
            }

            return value; // Exact length
        }

        // Get century digit (0-9) from date
        private int GetCentury(DateTime date)
        {
            int year = date.Year;
            return (year / 100) % 10; // Returns last digit of century (20 -> 0, 21 -> 1)
        }

        // Get province/state from retail event (for tax logic)
        private string? GetProvince(RetailEvent retailEvent)
        {
            // Try to extract province from tax area code
            // Common patterns: "ON" for Ontario, "QC" for Quebec, etc.
            string? taxArea = retailEvent.BusinessContext?.Store?.TaxArea;

            if (!string.IsNullOrEmpty(taxArea))
            {
                // If tax area starts with province code, extract it
                if (taxArea.Length >= 2)
                {
                    string prefix = taxArea.Substring(0, 2).ToUpper();

                    // Canadian provinces
                    if (prefix == "ON" || prefix == "QC" || prefix == "BC" ||
                        prefix == "AB" || prefix == "MB" || prefix == "SK" ||
                        prefix == "NS" || prefix == "NB" || prefix == "PE" ||
                        prefix == "NL" || prefix == "YT" || prefix == "NT" || prefix == "NU")
                    {
                        return prefix;
                    }
                }

                // Check if tax area contains province name
                string taxAreaUpper = taxArea.ToUpper();
                if (taxAreaUpper.Contains("ONTARIO") || taxAreaUpper.Contains("HON"))
                    return "ON";
                if (taxAreaUpper.Contains("QUEBEC"))
                    return "QC";
                if (taxAreaUpper.Contains("BRITISH") || taxAreaUpper.Contains("BC"))
                    return "BC";
            }

            // TODO: Add province field to Store class for explicit province detection
            // For now, return null if unable to detect
            return null;
        }

        // Check if item is an EPP (Extended Protection Plan)
        private bool IsEPPItem(TransactionItem item)
        {
            // Check if SKU indicates EPP
            if (item.Item?.Sku != null)
            {
                string sku = item.Item.Sku.ToUpper();
                if (sku.StartsWith("EPP") || sku.Contains("PROTECT") ||
                    sku.Contains("WARRANTY") || sku.StartsWith("WTY") ||
                    sku.StartsWith("WARR"))
                    return true;
            }

            // Check item description
            if (item.Item?.Description != null)
            {
                string desc = item.Item.Description.ToUpper();
                if (desc.Contains("PROTECTION PLAN") || desc.Contains("WARRANTY") ||
                    desc.Contains("EXTENDED PROTECTION") || desc.Contains("EPP"))
                    return true;
            }

            return false;
        }

        // Check if SKU is eligible for EPP coverage
        private bool IsSKUEligibleForEPP(string? sku)
        {
            // Business logic to determine if SKU can have EPP
            // For now, assume most items are eligible (this would be configured per business rules)
            // TODO: Implement SKU category lookup or eligibility table

            if (string.IsNullOrEmpty(sku))
                return false;

            // EPP items themselves are not eligible for EPP
            string skuUpper = sku.ToUpper();
            if (skuUpper.StartsWith("EPP") || skuUpper.StartsWith("WTY") || skuUpper.StartsWith("WARR"))
                return false;

            // Most merchandise items are eligible
            return true;
        }

        // Determine how many items an EPP covers
        private int DetermineEPPCoveredItemCount(TransactionItem eppItem, RetailEvent retailEvent)
        {
            // Business logic to determine coverage count
            // This could be based on:
            // - EPP quantity
            // - EPP metadata/description
            // - Transaction-level EPP associations

            // For now, use quantity as indicator
            if (eppItem.Quantity != null && eppItem.Quantity.Value > 1)
            {
                return (int)eppItem.Quantity.Value;
            }

            // Default to single item coverage
            return 1;
        }

        // Determine EPP eligibility digit for SLFZIP (last character)
        private string DetermineEPPEligibility(TransactionItem item, RetailEvent retailEvent)
        {
            // Check if this item IS the EPP
            if (IsEPPItem(item))
                return "9";

            // Check if EPP exists in transaction
            bool hasEPP = retailEvent.Transaction?.Items?.Any(i => IsEPPItem(i)) ?? false;

            if (!hasEPP)
            {
                // No EPP in transaction - check if this SKU is eligible
                if (IsSKUEligibleForEPP(item.Item?.Sku))
                    return "0"; // Eligible but no EPP
                else
                    return "0"; // Not eligible
            }

            // EPP exists - determine if this item is covered
            var eppItem = retailEvent.Transaction?.Items?.FirstOrDefault(i => IsEPPItem(i));

            if (eppItem != null)
            {
                int coveredItemCount = DetermineEPPCoveredItemCount(eppItem, retailEvent);

                if (coveredItemCount == 1)
                    return "1"; // Single item coverage
                else if (coveredItemCount > 1)
                    return "2"; // Multi-item coverage
            }

            // Default
            return "0";
        }

        // Determine tax authority code based on tax rate and jurisdiction
        private string DetermineTaxAuthority(RetailEvent retailEvent, decimal totalTax)
        {
            // Check item-level taxes for rate information
            if (retailEvent.Transaction?.Items != null)
            {
                foreach (var item in retailEvent.Transaction.Items)
                {
                    if (item.Taxes != null)
                    {
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxRate != null)
                            {
                                decimal rate = tax.TaxRate.Value;

                                // HST is 13% in Ontario
                                if (Math.Abs(rate - 0.13m) < 0.001m)
                                    return "HON"; // 13% HST

                                // GST is 5% federal
                                if (Math.Abs(rate - 0.05m) < 0.001m)
                                    return "HON1"; // 5% GST
                            }

                            // Check tax authority from tax detail
                            if (!string.IsNullOrEmpty(tax.TaxAuthority))
                                return PadOrTruncate(tax.TaxAuthority, 6) ?? "HON";
                        }
                    }
                }
            }

            // Calculate effective tax rate from totals
            // Calculate subtotal as: Net - Tax (pre-tax amount)
            if (retailEvent.Transaction?.Totals?.Net?.Value != null &&
                decimal.TryParse(retailEvent.Transaction.Totals.Net.Value, out decimal netAmount))
            {
                decimal subtotal = netAmount - totalTax;

                if (subtotal > 0)
                {
                    decimal effectiveRate = totalTax / subtotal;

                    // Check if rate is close to 13% (HST)
                    if (Math.Abs(effectiveRate - 0.13m) < 0.01m)
                        return "HON"; // ~13% = HST
                    // Check if rate is close to 5% (GST)
                    else if (Math.Abs(effectiveRate - 0.05m) < 0.01m)
                        return "HON1"; // ~5% = GST
                }
            }
            // Alternative: Calculate subtotal as Gross - Discounts
            else if (retailEvent.Transaction?.Totals?.Gross?.Value != null &&
                     decimal.TryParse(retailEvent.Transaction.Totals.Gross.Value, out decimal grossAmount))
            {
                decimal discountAmount = 0;
                if (retailEvent.Transaction?.Totals?.Discounts?.Value != null)
                {
                    decimal.TryParse(retailEvent.Transaction.Totals.Discounts.Value, out discountAmount);
                }

                decimal subtotal = grossAmount - discountAmount;

                if (subtotal > 0)
                {
                    decimal effectiveRate = totalTax / subtotal;

                    // Check if rate is close to 13% (HST)
                    if (Math.Abs(effectiveRate - 0.13m) < 0.01m)
                        return "HON"; // ~13% = HST
                    // Check if rate is close to 5% (GST)
                    else if (Math.Abs(effectiveRate - 0.05m) < 0.01m)
                        return "HON1"; // ~5% = GST
                }
            }

            // Check province to determine default
            string? province = GetProvince(retailEvent);
            if (province?.ToUpper() == "ON" || province?.ToUpper() == "ONTARIO")
                return "HON"; // Ontario defaults to HST

            // Default to HON for unknown cases
            return "HON";
        }
 
        private string MapTransTypeSLFTTP(string input, bool hasEmployeeDiscount)
        {
            // Employee sales get SLFTTP = "04"
            if (input == "SALE" && hasEmployeeDiscount)
            {
                return "04";
            }

            return input switch
            {
                "SALE" => "01",    // Regular sale (no employee discount)
                "RETURN" => "02",  // Return transactions
                "VOID" => "11",    // Void transactions
                "OPEN" => "87",
                "CLOSE" => "88",
                _ => "Unknown: " + input
            };
        }

        private string MapTransTypeSLFLNT(string input, bool hasEmployeeDiscount, bool hasGiftCardTender, bool hasCustomerId)
        {
            // Handle SALE transactions
            if (input == "SALE")
            {
                // Employee sales → SLFLNT = "04"
                if (hasEmployeeDiscount)
                {
                    return "04";
                }

                // Gift card sales → SLFLNT = "45"
                if (hasGiftCardTender)
                {
                    return "45";
                }

                // Regular trade (with customer) → SLFLNT = "02"
                if (hasCustomerId)
                {
                    return "02";
                }

                // Regular sales (no customer) → SLFLNT = "01"
                return "01";
            }

            // Handle RETURN transactions
            if (input == "RETURN")
            {
                // Gift card return → SLFLNT = "45"
                if (hasGiftCardTender)
                {
                    return "45";
                }

                // Trade return (with customer) → SLFLNT = "12"
                if (hasCustomerId)
                {
                    return "12";
                }

                // Regular return (no customer) → SLFLNT = "11"
                return "11";
            }

            // Handle other transaction types
            return input switch
            {
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
            // Fix for CS9035: Always set required Transaction property
            return retailEvent ?? new RetailEvent { Transaction = new Transaction { Items = new List<TransactionItem>(), Totals = new Totals() } };
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
            // Fix for CS9035: Always set required Transaction property
            return retailEvent ?? new RetailEvent { Transaction = new Transaction { Items = new List<TransactionItem>(), Totals = new Totals() } };
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

    // ORAE v2.0.0 Compliance Validation
    public static List<string> ValidateOraeCompliance(RetailEvent retailEvent)
    {
        var errors = new List<string>();

        // Required root fields
        if (string.IsNullOrEmpty(retailEvent.SchemaVersion))
            errors.Add("Missing required field: schemaVersion");
        else if (retailEvent.SchemaVersion != "2.0.0")
            errors.Add($"Invalid schemaVersion: {retailEvent.SchemaVersion}. Expected '2.0.0'");

        if (string.IsNullOrEmpty(retailEvent.MessageType))
            errors.Add("Missing required field: messageType");
        else if (retailEvent.MessageType != "RetailEvent")
            errors.Add($"Invalid messageType: {retailEvent.MessageType}. Expected 'RetailEvent'");

        if (string.IsNullOrEmpty(retailEvent.EventType))
            errors.Add("Missing required field: eventType");
        else if (!IsValidEnum(retailEvent.EventType, new[] { "ORIGINAL", "CORRECTION", "CANCELLATION", "SNAPSHOT" }))
            errors.Add($"Invalid eventType: {retailEvent.EventType}");

        if (string.IsNullOrEmpty(retailEvent.EventCategory))
            errors.Add("Missing required field: eventCategory");
        else if (!IsValidEnum(retailEvent.EventCategory, new[] {
            "TRANSACTION", "TILL", "STORE", "SESSION", "BANKING", "ORDER",
            "INVENTORY", "MAINTENANCE", "CALIBRATION", "AUDIT", "STATUS",
            "LOYALTY", "TOPUP", "PROMOTION", "OTHER" }))
            errors.Add($"Invalid eventCategory: {retailEvent.EventCategory}");

        if (string.IsNullOrEmpty(retailEvent.EventId))
            errors.Add("Missing required field: eventId");

        if (retailEvent.OccurredAt == default(DateTime))
            errors.Add("Missing required field: occurredAt");

        if (retailEvent.IngestedAt == default(DateTime))
            errors.Add("Missing required field: ingestedAt");

        // BusinessContext validation
        if (retailEvent.BusinessContext == null)
        {
            errors.Add("Missing required object: businessContext");
        }
        else
        {
            if (retailEvent.BusinessContext.BusinessDay == default(DateTime))
                errors.Add("Missing required field: businessContext.businessDay");

            if (retailEvent.BusinessContext.Store == null)
                errors.Add("Missing required object: businessContext.store");
            else if (string.IsNullOrEmpty(retailEvent.BusinessContext.Store.StoreId))
                errors.Add("Missing required field: businessContext.store.storeId");

            if (retailEvent.BusinessContext.Workstation == null)
                errors.Add("Missing required object: businessContext.workstation");
            else if (string.IsNullOrEmpty(retailEvent.BusinessContext.Workstation.RegisterId))
                errors.Add("Missing required field: businessContext.workstation.registerId");

            if (string.IsNullOrEmpty(retailEvent.BusinessContext.Channel))
                errors.Add("Missing required field: businessContext.channel");
        }

        // Category-specific payload validation
        if (retailEvent.EventCategory == "TRANSACTION" && retailEvent.Transaction == null)
            errors.Add("eventCategory 'TRANSACTION' requires transaction object");

        // Transaction validation
        if (retailEvent.Transaction != null)
        {
            if (string.IsNullOrEmpty(retailEvent.Transaction.TransactionType))
                errors.Add("Missing required field: transaction.transactionType");
            else if (!IsValidEnum(retailEvent.Transaction.TransactionType, new[] {
                "SALE", "RETURN", "EXCHANGE", "VOID", "CANCEL", "ADJUSTMENT", "NONMERCH", "SERVICE" }))
                errors.Add($"Invalid transaction.transactionType: {retailEvent.Transaction.TransactionType}");

            if (retailEvent.Transaction.Totals == null)
                errors.Add("Missing required object: transaction.totals");
            else
            {
                if (retailEvent.Transaction.Totals.Gross == null)
                    errors.Add("Missing required field: transaction.totals.gross");
                if (retailEvent.Transaction.Totals.Discounts == null)
                    errors.Add("Missing required field: transaction.totals.discounts");
                if (retailEvent.Transaction.Totals.Tax == null)
                    errors.Add("Missing required field: transaction.totals.tax");
                if (retailEvent.Transaction.Totals.Net == null)
                    errors.Add("Missing required field: transaction.totals.net");
            }

            // Items validation (must have at least 1 unless CANCEL/VOID)
            if (retailEvent.Transaction.TransactionType != "CANCEL" &&
                retailEvent.Transaction.TransactionType != "VOID" &&
                (retailEvent.Transaction.Items == null || retailEvent.Transaction.Items.Count == 0))
            {
                errors.Add("transaction.items[] must have at least one item for non-CANCEL/VOID transactions");
            }

            // Line item validation
            if (retailEvent.Transaction.Items != null)
            {
                for (int i = 0; i < retailEvent.Transaction.Items.Count; i++)
                {
                    var item = retailEvent.Transaction.Items[i];
                    if (string.IsNullOrEmpty(item.LineId))
                        errors.Add($"Missing required field: transaction.items[{i}].lineId");
                    if (item.Item == null)
                        errors.Add($"Missing required object: transaction.items[{i}].item");
                    else
                    {
                        if (string.IsNullOrEmpty(item.Item.Sku))
                            errors.Add($"Missing required field: transaction.items[{i}].item.sku");
                        if (string.IsNullOrEmpty(item.Item.Description))
                            errors.Add($"Missing required field: transaction.items[{i}].item.description");
                    }
                    if (item.Quantity == null)
                        errors.Add($"Missing required object: transaction.items[{i}].quantity");
                    else
                    {
                        if (string.IsNullOrEmpty(item.Quantity.Uom))
                            errors.Add($"Missing required field: transaction.items[{i}].quantity.uom");
                    }
                    if (item.Pricing == null)
                        errors.Add($"Missing required object: transaction.items[{i}].pricing");
                    else
                    {
                        if (item.Pricing.UnitPrice == null)
                            errors.Add($"Missing required field: transaction.items[{i}].pricing.unitPrice");
                        if (item.Pricing.ExtendedPrice == null)
                            errors.Add($"Missing required field: transaction.items[{i}].pricing.extendedPrice");
                    }
                }
            }

            // Tender validation
            if (retailEvent.Transaction.Tenders != null)
            {
                for (int i = 0; i < retailEvent.Transaction.Tenders.Count; i++)
                {
                    var tender = retailEvent.Transaction.Tenders[i];
                    if (string.IsNullOrEmpty(tender.TenderId))
                        errors.Add($"Missing required field: transaction.tenders[{i}].tenderId");
                    if (string.IsNullOrEmpty(tender.Method))
                        errors.Add($"Missing required field: transaction.tenders[{i}].method");
                    if (tender.Amount == null)
                        errors.Add($"Missing required field: transaction.tenders[{i}].amount");
                }
            }
        }

        return errors;
    }

    private static bool IsValidEnum(string value, string[] validValues)
    {
        return validValues.Contains(value);
    }

    // RecordSet Output Validation (RIMSLF/RIMTNF)
    public static List<string> ValidateRecordSetOutput(RecordSet recordSet)
    {
        var errors = new List<string>();

        if (recordSet == null)
        {
            errors.Add("RecordSet is null");
            return errors;
        }

        // Validate OrderRecords (RIMSLF)
        if (recordSet.OrderRecords != null)
        {
            for (int i = 0; i < recordSet.OrderRecords.Count; i++)
            {
                var order = recordSet.OrderRecords[i];
                string prefix = $"RIMSLF[{i}]";

                // Required fields
                if (string.IsNullOrEmpty(order.TransType))
                    errors.Add($"{prefix}: SLFTTP (TransType) is required");
                if (string.IsNullOrEmpty(order.LineType))
                    errors.Add($"{prefix}: SLFLNT (LineType) is required");
                if (string.IsNullOrEmpty(order.TransDate))
                    errors.Add($"{prefix}: SLFTDT (TransDate) is required");
                if (string.IsNullOrEmpty(order.TransTime))
                    errors.Add($"{prefix}: SLFTTM (TransTime) is required");
                if (string.IsNullOrEmpty(order.TransNumber))
                    errors.Add($"{prefix}: SLFTTX (TransNumber) is required");
                if (string.IsNullOrEmpty(order.TransSeq))
                    errors.Add($"{prefix}: SLFTSQ (TransSeq) is required");
                if (string.IsNullOrEmpty(order.RegisterID))
                    errors.Add($"{prefix}: SLFREG (RegisterID) is required");
                if (string.IsNullOrEmpty(order.SKUNumber))
                    errors.Add($"{prefix}: SLFSKU (SKUNumber) is required");
                if (order.PolledStore == 0)
                    errors.Add($"{prefix}: SLFPST (PolledStore) is required");
                // PollCen and CreateCen are century digits (0-9), so 0 is valid for 2000-2099
                if (order.PollCen < 0 || order.PollCen > 9)
                    errors.Add($"{prefix}: SLFPCN (PollCen) must be 0-9");
                if (order.PollDate == 0)
                    errors.Add($"{prefix}: SLFPDT (PollDate) is required");
                if (order.CreateCen < 0 || order.CreateCen > 9)
                    errors.Add($"{prefix}: SLFCCN (CreateCen) must be 0-9");
                if (order.CreateDate == 0)
                    errors.Add($"{prefix}: SLFCDT (CreateDate) is required");
                if (order.CreateTime == 0)
                    errors.Add($"{prefix}: SLFCTM (CreateTime) is required");

                // Pricing fields
                if (string.IsNullOrEmpty(order.Quantity))
                    errors.Add($"{prefix}: SLFQTY (Quantity) is required");
                if (string.IsNullOrEmpty(order.ItemSellPrice))
                    errors.Add($"{prefix}: SLFSEL (ItemSellPrice) is required");
                if (string.IsNullOrEmpty(order.ExtendedValue))
                    errors.Add($"{prefix}: SLFEXT (ExtendedValue) is required");

                // Validate all string field lengths using reflection
                ValidateStringFieldLengths(order, typeof(OrderRecord), prefix, errors);
            }
        }

        // Validate TenderRecords (RIMTNF)
        if (recordSet.TenderRecords != null)
        {
            for (int i = 0; i < recordSet.TenderRecords.Count; i++)
            {
                var tender = recordSet.TenderRecords[i];
                string prefix = $"RIMTNF[{i}]";

                // Required fields
                if (string.IsNullOrEmpty(tender.TransactionType))
                    errors.Add($"{prefix}: TNFTTP (TransactionType) is required");
                if (string.IsNullOrEmpty(tender.TransactionDate))
                    errors.Add($"{prefix}: TNFTDT (TransactionDate) is required");
                if (string.IsNullOrEmpty(tender.TransactionTime))
                    errors.Add($"{prefix}: TNFTTM (TransactionTime) is required");
                if (string.IsNullOrEmpty(tender.TransactionNumber))
                    errors.Add($"{prefix}: TNFTTX (TransactionNumber) is required");
                if (string.IsNullOrEmpty(tender.TransactionSeq))
                    errors.Add($"{prefix}: TNFTSQ (TransactionSeq) is required");
                if (string.IsNullOrEmpty(tender.RegisterID))
                    errors.Add($"{prefix}: TNFREG (RegisterID) is required");
                if (string.IsNullOrEmpty(tender.FundCode))
                    errors.Add($"{prefix}: TNFFCD (FundCode) is required");
                if (string.IsNullOrEmpty(tender.Amount))
                    errors.Add($"{prefix}: TNFAMT (Amount) is required");
                if (tender.PolledStore == 0)
                    errors.Add($"{prefix}: TNFPST (PolledStore) is required");
                // PollCen and CreateCen are century digits (0-9), so 0 is valid for 2000-2099
                if (tender.PollCen < 0 || tender.PollCen > 9)
                    errors.Add($"{prefix}: TNFPCN (PollCen) must be 0-9");
                if (tender.PollDate == 0)
                    errors.Add($"{prefix}: TNFPDT (PollDate) is required");
                if (tender.CreateCen < 0 || tender.CreateCen > 9)
                    errors.Add($"{prefix}: TNFCCN (CreateCen) must be 0-9");
                if (tender.CreateDate == 0)
                    errors.Add($"{prefix}: TNFCDT (CreateDate) is required");
                if (tender.CreateTime == 0)
                    errors.Add($"{prefix}: TNFCTM (CreateTime) is required");

                // Validate all string field lengths using reflection
                ValidateStringFieldLengths(tender, typeof(TenderRecord), prefix, errors);
            }
        }

        // Warn if both are empty
        if ((recordSet.OrderRecords == null || recordSet.OrderRecords.Count == 0) &&
            (recordSet.TenderRecords == null || recordSet.TenderRecords.Count == 0))
        {
            errors.Add("RecordSet contains no OrderRecords or TenderRecords");
        }

        return errors;
    }

    // Helper method to validate string field lengths based on StringLength attributes
    private static void ValidateStringFieldLengths(object record, Type recordType, string prefix, List<string> errors)
    {
        var properties = recordType.GetProperties();

        foreach (var prop in properties)
        {
            // Only validate string properties
            if (prop.PropertyType != typeof(string))
                continue;

            var stringLengthAttr = prop.GetCustomAttributes(typeof(StringLengthAttribute), false)
                                       .FirstOrDefault() as StringLengthAttribute;

            if (stringLengthAttr == null)
                continue;

            var jsonAttr = prop.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                              .FirstOrDefault() as JsonPropertyNameAttribute;
            string fieldName = jsonAttr?.Name ?? prop.Name;

            string? value = prop.GetValue(record) as string;
            int minLength = stringLengthAttr.MinimumLength;
            int maxLength = stringLengthAttr.MaximumLength;

            // Rule: For blank values, they can be zero length OR properly padded.
            //       For non-blank values, length must match exactly (MinimumLength == MaxLength due to padding)
            if (string.IsNullOrEmpty(value))
            {
                // Blank field - can be empty (length 0) or properly padded to minLength
                // No validation error for blank fields
                continue;
            }

            // Non-blank field - must match exact length (MinimumLength should equal MaxLength)
            if (value.Length < minLength)
            {
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but minimum is {minLength}");
            }
            else if (value.Length > maxLength)
            {
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but maximum is {maxLength}");
            }
            else if (minLength == maxLength && value.Length != minLength)
            {
                // When min==max, value must be exact length
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but must be exactly {minLength}");
            }
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
