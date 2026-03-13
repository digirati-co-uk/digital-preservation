using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public class FileLink
{
    [JsonPropertyName("to")]
    [JsonPropertyOrder(1)]
    public required string To { get; set; }
    
    [JsonPropertyName("role")]
    [JsonPropertyOrder(2)]
    public Uri? Role { get; set; }
}