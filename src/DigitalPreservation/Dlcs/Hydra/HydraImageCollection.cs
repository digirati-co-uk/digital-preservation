using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class HydraImageCollection : HydraCollectionBase
{
    [JsonPropertyName("member")]
    [JsonPropertyOrder(20)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Image[]? Members { get; set; }
}