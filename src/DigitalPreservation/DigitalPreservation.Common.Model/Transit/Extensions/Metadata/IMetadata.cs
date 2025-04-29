using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FileFormatMetadata), typeDiscriminator: "FileFormatMetadata")]
[JsonDerivedType(typeof(ExifMetadata), typeDiscriminator: "ExifMetadata")]
[JsonDerivedType(typeof(DigestMetadata), typeDiscriminator: "DigestMetadata")]
[JsonDerivedType(typeof(VirusScanMetadata), typeDiscriminator: "VirusScanMetadata")]
public abstract class Metadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(1)]
    public DateTime Timestamp { get; set; }
}