using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public abstract class JSONLDBase
{
    [JsonPropertyName("@context")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual string? Context { get; set; }

    [JsonPropertyName("@id")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("@type")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual string? Type { get; set; }
}