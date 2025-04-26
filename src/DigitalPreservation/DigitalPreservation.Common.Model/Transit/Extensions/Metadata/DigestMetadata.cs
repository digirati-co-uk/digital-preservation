using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class DigestMetadata : IMetadata, IDigestMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(2)]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("digest")]
    [JsonPropertyOrder(10)]
    public string? Digest { get; set; }
}