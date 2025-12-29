
using Google.Cloud.PubSub.V1;
using PubSubApp;
using System.Text.Json;

public class PubSubSubscriber : IPubSubSubscriber
{
    private readonly SubscriberClient _subscriber;
    private readonly PubSubConfiguration _config;
    private Func<PubsubMessage, CancellationToken, Task>? _messageHandler;

    public PubSubSubscriber(PubSubConfiguration config)
    {
        _config = config;
        var subscriptionName = SubscriptionName.FromProjectSubscription(config.ProjectId, config.SubscriptionId);

        var builder = new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            Settings = new SubscriberClient.Settings
            {
                AckExtensionWindow = TimeSpan.FromSeconds(30)
            }
        };

        _subscriber = builder.Build();
    }

    // Add method to set the handler
    public void SetMessageHandler(Func<PubsubMessage, CancellationToken, Task> handler)
    {
        _messageHandler = handler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _subscriber.StartAsync(async (msg, ct) =>
        {
            try
            {
                string data = msg.Data.ToStringUtf8();

                // Save to file using configured path with all message attributes
                if (!string.IsNullOrEmpty(_config.MessageSavePath))
                {
                    // Ensure directory exists
                    Directory.CreateDirectory(_config.MessageSavePath);

                    // Create a complete message envelope with all attributes
                    var messageEnvelope = new
                    {
                        MessageId = msg.MessageId,
                        PublishTime = msg.PublishTime.ToDateTime(),
                        OrderingKey = msg.OrderingKey,
                        Attributes = msg.Attributes,
                        Data = JsonSerializer.Deserialize<object>(data) // Parse the JSON data
                    };

                    string filePath = Path.Combine(_config.MessageSavePath, $"PubSubMessage_{DateTime.Now:yyyyMMddHHmmss}_{msg.MessageId}.json");
                    string jsonOutput = JsonSerializer.Serialize(messageEnvelope, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(filePath, jsonOutput);
                }
                Console.WriteLine($"Received: {msg.MessageId}");

                // Call custom handler if set
                if (_messageHandler != null)
                {
                    await _messageHandler(msg, ct);
                }
                else
                {
                    // Default behavior
                    await Task.Delay(100, ct);
                }

                return SubscriberClient.Reply.Ack;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Processing error: {ex.Message}");
                return SubscriberClient.Reply.Nack;
            }
        });

        Console.WriteLine("Subscriber started. Press Ctrl+C to stop.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (TaskCanceledException) { }
    }
}