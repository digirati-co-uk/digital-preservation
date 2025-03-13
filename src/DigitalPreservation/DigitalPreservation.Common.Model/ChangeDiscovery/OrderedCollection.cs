using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class OrderedCollection : ResourceWithContext
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public string Type => nameof(OrderedCollection);
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("partOf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollection? PartOf { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("totalItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalItems { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("first")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? First { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("last")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Last { get; set; }
}