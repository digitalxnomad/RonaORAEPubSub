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
        public string? TransType { get; set; }

        [JsonPropertyName("SLFLNT")]
        [Required]
        public string? LineType { get; set; }

        [JsonPropertyName("SLFTDT")]
        public string? TransDate { get; set; }

        [JsonPropertyName("SLFTTM")]
        public string? TransTime { get; set; }

        [JsonPropertyName("SLFPLC")]
        public int PolledStore { get; set; }

        [JsonPropertyName("SLFREG")]
        public string? RegisterID { get; set; }

        [JsonPropertyName("SLFTTX")]
        [Required]
        public string? TransNumber { get; set; }

        [JsonPropertyName("SLFTSQ")]
        [Required]
        public string? TransSeq { get; set; }

        [JsonPropertyName("SLFSKU")]
        public string? SKUNumber { get; set; }

        [JsonPropertyName("SLFORG")]
        public string? OriginalPrice { get; set; }

        [JsonPropertyName("SLFQTY")]
        public string? Quantity { get; set; }
    }

    // A TenderRecord is an object meant for direct translation to a SQL command against the SDITNF table in MMS
    public class TenderRecord
    {
        [JsonPropertyName("TNFTTP")]
        [Required]
        public string? TransactionType { get; set; }

        [JsonPropertyName("TNFTDT")]
        public string? TransactionDate { get; set; }

        [JsonPropertyName("TNFTTM")]
        public string? TransactionTime { get; set; }

        [JsonPropertyName("TNFCLK")]
        public string? Clerk { get; set; }

        [JsonPropertyName("TNFREG")]
        public string? RegisterID { get; set; }

        [JsonPropertyName("TNFTTX")]
        [Required]
        public string? TransactionNumber { get; set; }

        [JsonPropertyName("TNFTSQ")]
        [Required]
        public string? TransactionSeq { get; set; }

        [JsonPropertyName("TNFFCD")]
        public string? FundCode { get; set; }

        [JsonPropertyName("TNFAMT")]
        public string? Amount { get; set; }

        [JsonPropertyName("TNFPLC")]
        public string? PolledStore { get; set; }
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
        [JsonPropertyName("uom")]
        public string? Uom { get; set; }

        [JsonPropertyName("value")]
        public int Value { get; set; }
    }

    public class Totals
    {
        [JsonPropertyName("discounts")]
        public CurrencyAmount? Discounts { get; set; }

        [JsonPropertyName("gross")]
        public CurrencyAmount? Gross { get; set; }

        [JsonPropertyName("net")]
        public CurrencyAmount? Net { get; set; }

        [JsonPropertyName("tax")]
        public CurrencyAmount? Tax { get; set; }
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
        
        var recordSet = new RecordSet
            {
           
                OrderRecord = new OrderRecord
                {
                    TransDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyyyMMdd"),
                    TransTime = retailEvent.OccurredAt.ToString("HHmmss"),
                    TransNumber = retailEvent.EventId,
                    RegisterID = retailEvent.BusinessContext?.Workstation?.RegisterId,
                    TransType = mappedTransactionTypeSLFLNT,
                    LineType = "ITEM",
                    TransSeq = "1"
                },
                TenderRecord = new TenderRecord
                {
                    TransactionDate = retailEvent.BusinessContext?.BusinessDay.ToString("yyyyMMdd"),
                    TransactionTime = retailEvent.OccurredAt.ToString("HHmmss"),
                    TransactionNumber = retailEvent.EventId,
                    RegisterID = retailEvent.BusinessContext?.Workstation?.RegisterId,
                    TransactionType = mappedTransactionTypeSLFTTP,
                    Amount = retailEvent.Transaction?.Totals?.Net?.Value,
                    PolledStore = retailEvent.BusinessContext?.Store?.StoreId,
                    TransactionSeq = "1"
                }
            };

            // Map first item if exists
            if (retailEvent.Transaction?.Items != null && retailEvent.Transaction.Items.Count > 0)
            {
                var firstItem = retailEvent.Transaction.Items[0];
                recordSet.OrderRecord.SKUNumber = firstItem.Item?.Sku;
                recordSet.OrderRecord.OriginalPrice = firstItem.Pricing?.OriginalUnitPrice?.Value;
                recordSet.OrderRecord.Quantity = firstItem.Quantity?.Value.ToString();

                // Try to parse storeId as int if possible
                if (int.TryParse(retailEvent.BusinessContext?.Store?.StoreId, out int storeId))
                {
                    recordSet.OrderRecord.PolledStore = storeId;
                }
            }

            return recordSet;
        }
    
        private string MapTransTypeSLFTTP(string input)
        {
            return input switch
            {
                "SALE" => "01",
                "RETURN" => "11",
                "VOID" => "87",
                _ => "Unknown: " + input
            };
        }

        private string MapTransTypeSLFLNT(string input)
        {
            return input switch
            {
                "SALE" => "01",
                "RETURN" => "11",
                "VOID" => "87",
                _ => "Unknown: " + input
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
