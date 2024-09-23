using System.Text.Json.Serialization;

namespace Storage.API.Fedora.Model;

public class BinaryMetadataResponse : FedoraJsonLdResponse
{
    /// <summary>
    /// This property is present at ebucore:filename in Fedora RDF.
    /// It is present if you gave the binary a content disposition header to set the file name
    /// </summary>
    [JsonPropertyName("filename")]
    [JsonPropertyOrder(201)]
    public string? FileName { get; set; }

    [JsonPropertyName("hasMimeType")]
    [JsonPropertyOrder(202)]
    public string? ContentType { get; set; }

    // This comes back as a string
    [JsonPropertyName("hasSize")]
    [JsonPropertyOrder(203)]
    public string? Size { get; set; }

    [JsonPropertyName("hasMessageDigest")]
    [JsonPropertyOrder(211)]
    public string? Digest { get; set; }        
}