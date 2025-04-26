using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

/// <summary>
/// Represents only the Premis fields we are interested in WRITING to METS
/// </summary>
public class PremisMetadata : IMetadata, IDigestMetadata
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(1)]
    public required string Source { get; set; }
    
    [JsonPropertyName("timestamp")]
    [JsonPropertyOrder(2)]
    public DateTime Timestamp { get; set; }
    
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
    
    public string GetDisplay()
    {
        return $"{PronomKey}: {FormatName}";
    }
    
}