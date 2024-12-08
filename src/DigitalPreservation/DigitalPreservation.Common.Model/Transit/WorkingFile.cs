using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public class WorkingFile : WorkingBase
{
    [JsonPropertyName("contentType")]
    [JsonPropertyOrder(14)]
    public required string ContentType { get; set; }

    [JsonPropertyName("digest")]
    [JsonPropertyOrder(15)]
    public string? Digest { get; set; }
    
    [JsonPropertyName("size")]
    [JsonPropertyOrder(16)]
    public long? Size { get; set; }
}