using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class ActivityObject
{
    public ActivityObject()
    {
    }

    public ActivityObject(Uri id, string type)
    {
        Id = id;
        Type = type;
    }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    
    
    [JsonPropertyOrder(200)]
    [JsonPropertyName("seeAlso")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ActivityObject>? SeeAlso { get; set; }
}
