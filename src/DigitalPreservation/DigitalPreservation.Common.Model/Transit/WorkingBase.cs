using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

public abstract class WorkingBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public abstract string Type { get; set; }
    
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
    
    // METS-specific information
    
    [JsonPropertyName("physDivId")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhysDivId { get; set; }
    
    [JsonPropertyName("admId")]
    [JsonPropertyOrder(102)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdmId { get; set; }
    
    [JsonPropertyName("rightsStatement")]
    [JsonPropertyOrder(103)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RightsStatement { get; set; }
    
    [JsonPropertyName("accessCondition")]
    [JsonPropertyOrder(104)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessCondition { get; set; }
    
}