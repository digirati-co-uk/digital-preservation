using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public class Container : PreservedResource
{
    public override string Type { get; set; } = nameof(Container);

    [JsonPropertyName("containers")]
    [JsonPropertyOrder(300)]
    public Container[]? Containers { get; set; } = [];
    
    [JsonPropertyName("containerPager")]
    [JsonPropertyOrder(301)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QueryStringPager? ContainerPager { get; set; }
    
    [JsonPropertyName("binaries")]
    [JsonPropertyOrder(310)]
    public Binary[]? Binaries { get; set; } = [];
    
    public override string ToString()
    {
        return $"📁 {Name ?? GetSlug() ?? GetType().Name}";
    }
}