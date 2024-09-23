using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public abstract class PreservedResource : Resource
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    
    [JsonPropertyName("partOf")]
    [JsonPropertyOrder(50)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? PartOf { get; set; }
    
    [JsonPropertyOrder(200)]
    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Origin { get; set; }

    protected string? GetSlug()
    {
        return Id != null ? Id.Segments[^1] : null;
    }

    public const string BasePathElement = "repository";
}