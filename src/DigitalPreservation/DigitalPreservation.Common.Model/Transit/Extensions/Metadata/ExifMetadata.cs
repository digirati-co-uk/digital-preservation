using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : Metadata
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("tags")]
    public List<ExifTag>? Tags { get; set; } = [];
}

public class ExifTag
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("tagName")]
    public string? TagName { get; set; } = string.Empty;
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("tagValue")]
    public string? TagValue { get; set; } = string.Empty;
    
    [JsonPropertyOrder(3)]
    [JsonPropertyName("mismatchAdded")]
    public bool? MismatchAdded { get; set; } = false;
}