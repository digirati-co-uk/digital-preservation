using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public class Binary : PreservedResource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(Binary); 
    
    [JsonPropertyName("contentType")]
    [JsonPropertyOrder(300)]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    [JsonPropertyOrder(310)]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    [JsonPropertyOrder(320)]
    public string? Digest { get; set; }

    [JsonIgnore]
    public override string StringIcon => "🗎";
}