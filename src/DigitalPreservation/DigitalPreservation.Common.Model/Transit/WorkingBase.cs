using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public abstract class WorkingBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public abstract string Type { get; set; }
    
    /// <summary>
    /// This is always a file system rather than a URI path, and may contain characters acceptable in a file name
    /// but not a URI.
    /// </summary>
    [JsonPropertyName("localPath")]
    [JsonPropertyOrder(1)]
    public required string LocalPath { get; set; }
    
    [JsonPropertyName("name")]
    [JsonPropertyOrder(2)]
    public string? Name { get; set; }
    
    [JsonPropertyName("modified")]
    [JsonPropertyOrder(3)]
    public DateTime Modified { get; set; }
    
    // METS-specific information
    [JsonPropertyName("metsExtensions")]
    [JsonPropertyOrder(100)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetsExtensions? MetsExtensions { get; set; }
    
    public List<Metadata> Metadata { get; set; } = [];
    
    [JsonPropertyName("accessRestrictions")]
    [JsonPropertyOrder(5)]
    public List<string> AccessRestrictions { get; set; } = [];
    
    [JsonPropertyName("rightsStatement")]
    [JsonPropertyOrder(6)]
    public Uri? RightsStatement { get; set; }
    

    public string GetSlug()
    {
        return LocalPath.Split('/')[^1];
    }
}