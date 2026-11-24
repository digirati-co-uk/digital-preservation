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

    /// <summary>
    /// Where the Storage API serves this file. The Preservation API has no content route and
    /// removes this, so it is left out of the JSON when it is not set (#300).
    /// </summary>
    [JsonPropertyName("content")]
    [JsonPropertyOrder(500)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Content { get; set; }
    
    [JsonIgnore]
    public override string StringIcon => Icon;
    
    [JsonIgnore]
    public static string Icon => "🗎";
}