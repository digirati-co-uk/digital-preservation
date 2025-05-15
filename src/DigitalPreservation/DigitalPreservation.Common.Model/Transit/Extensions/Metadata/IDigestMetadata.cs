using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public interface IDigestMetadata : IMetadata
{
    [JsonPropertyName("digest")]
    [JsonPropertyOrder(10)]
    string? Digest { get; set; }
}