namespace PubSubApp;

public class PubSubConfiguration
{
    public string ProjectId { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string MessageSavePath { get; set; } = string.Empty;
    public string OutputSavePath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public bool DisableOraeValidation { get; set; } = false;
    public bool EnableDebugLogging { get; set; } = false;
    public int IdleTimeoutMinutes { get; set; } = 30;
}
