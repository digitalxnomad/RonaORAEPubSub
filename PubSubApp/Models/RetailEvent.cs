using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSubApp.Models;

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

    [JsonPropertyName("promotions")]
    public List<JsonElement>? Promotions { get; set; }

    [JsonPropertyName("actor")]
    public Actor? Actor { get; set; }
}
