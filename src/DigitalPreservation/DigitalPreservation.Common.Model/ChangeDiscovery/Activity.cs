using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.ChangeDiscovery;

public abstract class Activity
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public required Uri Id { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public abstract string Type { get; }
    
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EndTime { get; set; }
}