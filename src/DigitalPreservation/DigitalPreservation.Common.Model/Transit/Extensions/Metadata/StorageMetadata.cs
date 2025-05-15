using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class StorageMetadata : Metadata, IStorageMetadata
{
    [JsonPropertyName("originalName")]
    [JsonPropertyOrder(140)]
    public string? OriginalName { get; set; }
    
    [JsonPropertyName("storageLocation")]
    [JsonPropertyOrder(145)]
    public Uri? StorageLocation { get; set; }
}