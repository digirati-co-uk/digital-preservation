using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class VirusScanMetadata : IMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(2)]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("hasVirus")]
    [JsonPropertyOrder(110)]
    public bool HasVirus { get; set; }
    
    public string GetDisplay()
    {
        return HasVirus ? "☣" : ""; // ""✅"; too noisy
    }
}