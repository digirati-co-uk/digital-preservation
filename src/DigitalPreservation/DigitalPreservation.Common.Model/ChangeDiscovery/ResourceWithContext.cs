using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class ResourceWithContext
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("@context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; set; }

    public void WithContext()
    {
        Context ??= "http://iiif.io/api/discovery/1/context.json";
    }
}