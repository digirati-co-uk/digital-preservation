using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.DepositHelpers;

public class MinimalItem
{
    [JsonPropertyName("path")]
    [JsonPropertyOrder(1)]
    public required string RelativePath { get; set; }
    
    [JsonPropertyName("isDir")]
    [JsonPropertyOrder(2)]
    public bool IsDirectory { get; set; }
    
    [JsonPropertyName("where")]
    [JsonPropertyOrder(3)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Whereabouts Whereabouts { get; set; }
}