using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class DigestMetadata : Metadata, IDigestMetadata
{
    [JsonPropertyName("digest")]
    [JsonPropertyOrder(10)]
    public string? Digest { get; set; }
}