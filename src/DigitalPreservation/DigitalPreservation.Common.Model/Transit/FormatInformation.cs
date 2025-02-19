using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public class FormatInformation
{
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public string? Id { get; set; }
    
    [JsonPropertyName("label")]
    [JsonPropertyOrder(2)]
    public string? Label { get; set; }
}