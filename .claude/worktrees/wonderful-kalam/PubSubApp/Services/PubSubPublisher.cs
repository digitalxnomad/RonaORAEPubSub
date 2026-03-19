
using Google.Cloud.PubSub.V1;
using Google.Api.Gax.Grpc;  // For BatchingSettings
using Google.Protobuf;
using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Api.Gax;
using PubSubApp;

public class PubSubPublisher : IPubSubPublisher
{
    private readonly PublisherClient _publisher;

    public PubSubPublisher(PubSubConfiguration config)
    {
        var topicName = TopicName.FromProjectTopic(config.ProjectId, config.TopicId);

        var builder = new PublisherClientBuilder
        {
            TopicName = topicName,
            Settings = new PublisherClient.Settings
            {
                BatchingSettings = new BatchingSettings(
                    elementCountThreshold: 100,
                    byteCountThreshold: 1_000_000,
                    delayThreshold: TimeSpan.FromMilliseconds(100)
                )
            }
        };

        _publisher = builder.Build();
    }

    public async Task PublishMessageAsync(string message, IDictionary<string, string>? attributes = null)
    {
        var msg = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(message)
        };
        if (attributes != null)
        {
            foreach (var kv in attributes)
                msg.Attributes[kv.Key] = kv.Value;
        }

        try
        {
            string id = await _publisher.PublishAsync(msg);
            Console.WriteLine($"Published message ID {id}");
            
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine($"Publish error: {ex}");
        }
    }
}
