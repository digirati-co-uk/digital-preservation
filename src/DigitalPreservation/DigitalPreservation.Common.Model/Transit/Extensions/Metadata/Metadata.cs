using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FileFormatMetadata), typeDiscriminator: "FileFormatMetadata")]
[JsonDerivedType(typeof(ExifMetadata), typeDiscriminator: "ExifMetadata")]
[JsonDerivedType(typeof(DigestMetadata), typeDiscriminator: "DigestMetadata")]
[JsonDerivedType(typeof(VirusScanMetadata), typeDiscriminator: "VirusScanMetadata")]
[JsonDerivedType(typeof(ToolOutput), typeDiscriminator: "ToolOutput")]
[JsonDerivedType(typeof(StorageMetadata), typeDiscriminator: "StorageMetadata")]
public abstract class Metadata : IMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(1)]
    public DateTime Timestamp { get; set; }
}


public interface IMetadata
{
    string Source { get; set; }
    DateTime Timestamp { get; set; }
}