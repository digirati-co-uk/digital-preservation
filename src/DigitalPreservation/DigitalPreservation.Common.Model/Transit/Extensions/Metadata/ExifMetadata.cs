using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : IMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(2)]
    public DateTime Timestamp { get; set; }
}