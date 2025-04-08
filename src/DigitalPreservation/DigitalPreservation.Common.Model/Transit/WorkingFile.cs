using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Common.Model.Transit;

public class WorkingFile : WorkingBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(WorkingFile); 
    
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


