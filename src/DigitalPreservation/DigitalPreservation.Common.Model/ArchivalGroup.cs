using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Storage;

namespace DigitalPreservation.Common.Model;

public class ArchivalGroup : Container
{
    public override string Type { get; set; } = nameof(ArchivalGroup);
    
    [JsonPropertyName("version")]
    [JsonPropertyOrder(2)]
    public ObjectVersion? Version { get; set; }

    [JsonPropertyName("versions")]
    [JsonPropertyOrder(3)]
    public ObjectVersion[]? Versions { get; set; }

    [JsonPropertyName("storageMap")]
    [JsonPropertyOrder(101)]
    public StorageMap? StorageMap { get; set; }
    
    Here:  
    // This is not in the RFC docs    
    
    // grr we can't do this because for a new one there is no AG! We need to get the URL someother way and pass it to the mediatr
    [JsonPropertyName("deposits")]
    [JsonPropertyOrder(110)]
    public Uri? Deposits { get; set; }
        
    public override string StringIcon => "📦";
}