using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public abstract class WorkingBase
{
    [JsonPropertyName("localPath")]
    [JsonPropertyOrder(1)]
    public required string LocalPath { get; set; }
    
    [JsonPropertyName("name")]
    [JsonPropertyOrder(2)]
    public string? Name { get; set; }
    
    [JsonPropertyName("modified")]
    [JsonPropertyOrder(3)]
    public DateTime Modified { get; set; }

    public string GetSlug()
    {
        return LocalPath.Split('/')[^1];
    }
    
}