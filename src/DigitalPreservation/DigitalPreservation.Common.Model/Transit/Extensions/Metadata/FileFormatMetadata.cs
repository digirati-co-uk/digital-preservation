using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

/// <summary>
/// Represents only the Premis fields we are interested in WRITING to METS
/// </summary>
public class FileFormatMetadata : Metadata, IDigestMetadata, IStorageMetadata
{
    [JsonPropertyName("digest")]
    [JsonPropertyOrder(10)]
    public string? Digest { get; set; } // must be sha256; also on its own on 
    
    [JsonPropertyName("size")]
    [JsonPropertyOrder(110)]
    public long? Size { get; set; }
    
    [JsonPropertyName("pronomKey")]
    [JsonPropertyOrder(120)]
    
    public string? PronomKey { get; set; }
    [JsonPropertyName("formatName")]
    [JsonPropertyOrder(130)]
    public string? FormatName { get; set; }
    
    [JsonPropertyName("originalName")]
    [JsonPropertyOrder(140)]
    public string? OriginalName { get; set; }
    
    [JsonPropertyName("storageLocation")]
    [JsonPropertyOrder(145)]
    public Uri? StorageLocation { get; set; }
    
    [JsonPropertyName("contentType")]
    [JsonPropertyOrder(150)]
    public string? ContentType { get; set; }
    
    public string GetDisplay()
    {
        return $"{PronomKey}: {FormatName}";
    }
    
}