using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Storage.Ocfl;

public class Inventory
{
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public required string Id { get; set; }

    [JsonPropertyName("digestAlgorithm")]
    [JsonPropertyOrder(3)]
    public required string DigestAlgorithm { get; set; }

    [JsonPropertyName("head")]
    [JsonPropertyOrder(4)]
    public required string Head { get; set; }

    [JsonPropertyName("contentDirectory")]
    [JsonPropertyOrder(5)]
    public required string ContentDirectory { get; set; }

    [JsonPropertyName("manifest")]
    [JsonPropertyOrder(6)]
    public required Dictionary<string, string[]> Manifest { get; set; }

    [JsonPropertyName("versions")]
    [JsonPropertyOrder(7)]
    public required Dictionary<string, Version> Versions { get; set; }

}