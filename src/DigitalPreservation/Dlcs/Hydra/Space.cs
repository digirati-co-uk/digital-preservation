using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class Space : JSONLDBase
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
}