using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class OrderedCollectionPage : ResourceWithContext
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public string Type => nameof(OrderedCollectionPage);
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("startIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StartIndex { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("partOf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollection? PartOf { get; set; }
    
    [JsonPropertyOrder(20)]
    [JsonPropertyName("prev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Prev { get; set; }
    
    [JsonPropertyOrder(30)]
    [JsonPropertyName("next")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedCollectionPage? Next { get; set; }
    
    [JsonPropertyOrder(50)]
    [JsonPropertyName("orderedItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Activity>? OrderedItems { get; set; } 
}