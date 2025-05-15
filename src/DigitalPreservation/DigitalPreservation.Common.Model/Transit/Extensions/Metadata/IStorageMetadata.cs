using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public interface IStorageMetadata
{
    [JsonPropertyName("originalName")]
    [JsonPropertyOrder(140)]
    string? OriginalName { get; set; }
    
    [JsonPropertyName("storageLocation")]
    [JsonPropertyOrder(10)]
    Uri? StorageLocation { get; set; }
}