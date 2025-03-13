using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public class Activity
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActivityTypes Type { get; set;  }
    
    [JsonPropertyOrder(5)]
    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }
    
    [JsonPropertyOrder(10)]
    [JsonPropertyName("object")]
    public required ActivityObject Object { get; set; }
    
    [JsonPropertyOrder(100)]
    [JsonPropertyName("startTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartTime { get; set; }
    
    [JsonPropertyOrder(110)]
    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }
}

public enum ActivityTypes
{
    Add,
    Create,
    Delete,
    Move,
    Refresh,
    Remove,
    Update
}