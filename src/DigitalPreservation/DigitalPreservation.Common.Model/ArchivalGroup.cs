using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Storage;

namespace DigitalPreservation.Common.Model;

public class ArchivalGroup : Container
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(ArchivalGroup);
    
    [JsonPropertyName("version")]
    [JsonPropertyOrder(10)]
    public ObjectVersion? Version { get; set; }

    [JsonPropertyName("versions")]
    [JsonPropertyOrder(20)]
    public ObjectVersion[]? Versions { get; set; }

    [JsonPropertyName("storageMap")]
    [JsonPropertyOrder(101)]
    public StorageMap? StorageMap { get; set; }
    
    [JsonIgnore]
    public override string StringIcon => Icon;
    
    [JsonIgnore]
    public static string Icon => "📦";
}