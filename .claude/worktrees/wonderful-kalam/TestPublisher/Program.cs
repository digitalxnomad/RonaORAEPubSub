using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

class Program
{
    static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var projectId = configuration["PubSubConfiguration:ProjectId"];
        var inputTopicId = configuration["PubSubConfiguration:InputTopicId"] ?? "transaction-tree-input";

        if (string.IsNullOrEmpty(projectId))
        {
            Console.WriteLine("Error: ProjectId not configured in appsettings.json");
            return;
        }

        Console.WriteLine("=== PubSub Test Publisher ===");
        Console.WriteLine($"Project: {projectId}");
        Console.WriteLine($"Topic: {inputTopicId}\n");

        // Check for file argument
        if (args.Length > 0)
        {
            // Publish from file(s)
            foreach (var filePath in args)
            {
                if (File.Exists(filePath))
                {
                    await PublishFromFile(projectId, inputTopicId, filePath);
                }
                else
                {
                    Console.WriteLine($"File not found: {filePath}");
                }
            }
        }
        else
        {
            // Interactive mode
            Console.WriteLine("Usage:");
            Console.WriteLine("  TestPublisher <json-file>       Publish a JSON file");
            Console.WriteLine("  TestPublisher <file1> <file2>   Publish multiple files");
            Console.WriteLine("  TestPublisher                   Interactive mode\n");

            Console.WriteLine("Interactive mode - Enter commands:");
            Console.WriteLine("  file <path>    Publish a JSON file");
            Console.WriteLine("  sample         Publish a sample transaction");
            Console.WriteLine("  quit           Exit\n");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = input.Split(' ', 2);
                var command = parts[0].ToLower();

                switch (command)
                {
                    case "quit":
                    case "exit":
                    case "q":
                        Console.WriteLine("Goodbye!");
                        return;

                    case "file":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("Usage: file <path>");
                            break;
                        }
                        var path = parts[1].Trim('"');
                        if (File.Exists(path))
                        {
                            await PublishFromFile(projectId, inputTopicId, path);
                        }
                        else
                        {
                            Console.WriteLine($"File not found: {path}");
                        }
                        break;

                    case "sample":
                        await PublishSampleTransaction(projectId, inputTopicId);
                        break;

                    default:
                        // Try as file path
                        if (File.Exists(input))
                        {
                            await PublishFromFile(projectId, inputTopicId, input);
                        }
                        else
                        {
                            Console.WriteLine($"Unknown command: {command}");
                        }
                        break;
                }
            }
        }
    }

    static async Task PublishFromFile(string projectId, string topicId, string filePath)
    {
        try
        {
            var topicName = TopicName.FromProjectTopic(projectId, topicId);
            var publisher = await PublisherClient.CreateAsync(topicName);

            var json = await File.ReadAllTextAsync(filePath);

            var message = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(json)
            };

            // Add some attributes
            message.Attributes["source"] = "TestPublisher";
            message.Attributes["filename"] = Path.GetFileName(filePath);
            message.Attributes["timestamp"] = DateTime.UtcNow.ToString("O");

            var messageId = await publisher.PublishAsync(message);

            Console.WriteLine($"✓ Published: {Path.GetFileName(filePath)}");
            Console.WriteLine($"  MessageId: {messageId}");
            Console.WriteLine($"  Size: {json.Length} bytes\n");

            await publisher.ShutdownAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error publishing {filePath}: {ex.Message}");
        }
    }

    static async Task PublishSampleTransaction(string projectId, string topicId)
    {
        var sampleJson = @"{
  ""eventId"": ""test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + @""",
  ""eventType"": ""TRANSACTION"",
  ""occurredAt"": """ + DateTime.UtcNow.ToString("O") + @""",
  ""businessContext"": {
    ""store"": {
      ""storeId"": ""12345"",
      ""storeName"": ""Test Store"",
      ""taxArea"": ""ON""
    },
    ""workstation"": {
      ""registerId"": ""001"",
      ""sequenceNumber"": ""00001"",
      ""terminalType"": ""SCO""
    }
  },
  ""transaction"": {
    ""transactionId"": ""TXN-" + DateTime.Now.ToString("yyyyMMddHHmmss") + @""",
    ""transactionType"": ""SALE"",
    ""items"": [
      {
        ""itemId"": ""SKU123456"",
        ""description"": ""Test Item"",
        ""quantity"": {
          ""value"": ""1"",
          ""unitOfMeasure"": ""EA""
        },
        ""pricing"": {
          ""unitPrice"": { ""value"": ""19.99"", ""currency"": ""CAD"" },
          ""extendedPrice"": { ""value"": ""19.99"", ""currency"": ""CAD"" }
        },
        ""taxes"": [
          {
            ""taxType"": ""HST"",
            ""taxCode"": ""HST"",
            ""taxAmount"": { ""value"": ""2.60"", ""currency"": ""CAD"" },
            ""taxRate"": 0.13
          }
        ]
      }
    ],
    ""totals"": {
      ""subtotal"": { ""value"": ""19.99"", ""currency"": ""CAD"" },
      ""tax"": { ""value"": ""2.60"", ""currency"": ""CAD"" },
      ""net"": { ""value"": ""22.59"", ""currency"": ""CAD"" }
    },
    ""tenders"": [
      {
        ""tenderId"": ""T001"",
        ""method"": ""CREDIT"",
        ""cardScheme"": ""VISA"",
        ""amount"": { ""value"": ""22.59"", ""currency"": ""CAD"" }
      }
    ]
  }
}";

        try
        {
            var topicName = TopicName.FromProjectTopic(projectId, topicId);
            var publisher = await PublisherClient.CreateAsync(topicName);

            var message = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(sampleJson)
            };

            message.Attributes["source"] = "TestPublisher";
            message.Attributes["type"] = "sample";
            message.Attributes["timestamp"] = DateTime.UtcNow.ToString("O");

            var messageId = await publisher.PublishAsync(message);

            Console.WriteLine($"✓ Published sample transaction");
            Console.WriteLine($"  MessageId: {messageId}");
            Console.WriteLine($"  Size: {sampleJson.Length} bytes\n");

            await publisher.ShutdownAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error publishing sample: {ex.Message}");
        }
    }
}
