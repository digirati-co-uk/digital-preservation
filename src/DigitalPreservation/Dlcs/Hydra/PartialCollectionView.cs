using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class PartialCollectionView : JSONLDBase
{
    public override string Context => "http://www.w3.org/ns/hydra/context.jsonld";

    public override string Type => "PartialCollectionView";

    [JsonPropertyName("first")]
    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? First { get; set; }

    [JsonPropertyName("previous")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Previous { get; set; }

    [JsonPropertyName("next")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Next { get; set; }

    [JsonPropertyName("last")]
    [JsonPropertyOrder(14)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Last { get; set; }
}