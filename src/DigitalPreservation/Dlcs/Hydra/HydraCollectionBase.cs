using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class HydraCollectionBase : JSONLDBase
{
    public override string Context => "http://www.w3.org/ns/hydra/context.jsonld";
    public override string Type => "Collection";

    [JsonPropertyName("totalItems")]
    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalItems { get; set; }

    [JsonPropertyName("pageSize")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageSize { get; set; }

    [JsonPropertyName("view")]
    [JsonPropertyOrder(110)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PartialCollectionView? View { get; set; }
}