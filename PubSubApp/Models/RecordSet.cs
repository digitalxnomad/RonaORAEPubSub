using System.Text.Json.Serialization;

namespace PubSubApp.Models;

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
