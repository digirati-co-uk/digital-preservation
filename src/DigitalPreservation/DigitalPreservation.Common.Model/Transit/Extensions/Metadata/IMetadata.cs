using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public interface IMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(1)]
    DateTime Timestamp { get; set; }
}