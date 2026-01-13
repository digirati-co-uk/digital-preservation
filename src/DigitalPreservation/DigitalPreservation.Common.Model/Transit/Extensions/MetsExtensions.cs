using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class MetsExtensions
{
    [JsonPropertyName("href")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; set; }
    
    [JsonPropertyName("divId")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DivId { get; set; }
    
    [JsonPropertyName("admId")]
    [JsonPropertyOrder(102)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdmId { get; set; }
}