using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public class RecordInfo
{
    [JsonPropertyName("recordIdentifiers")]
    [JsonPropertyOrder(1)]
    public List<RecordIdentifier> RecordIdentifiers { get; set; } = [];
}

public class RecordIdentifier
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source  { get; set; }
    
    [JsonPropertyName("value")]
    [JsonPropertyOrder(2)]
    public required string Value { get; set; }
}