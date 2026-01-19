using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

namespace DigitalPreservation.Common.Model.Transit;

/// <summary>
/// Base class for physical resources in METS that have a local file path
/// </summary>
public abstract class WorkingBase : ResourceBase
{
    /// <summary>
    /// This is always a file system rather than a URI path, and may contain characters acceptable in a file name
    /// but not a URI.
    /// </summary>
    [JsonPropertyName("localPath")]
    [JsonPropertyOrder(1)]
    public required string LocalPath { get; set; }
    
    [JsonPropertyName("modified")]
    [JsonPropertyOrder(3)]
    public DateTime Modified { get; set; }
    
    [JsonPropertyName("metadata")]
    [JsonPropertyOrder(200)]
    public List<Metadata> Metadata { get; set; } = [];
    
    public string GetSlug()
    {
        return LocalPath.Split('/')[^1];
    }
}