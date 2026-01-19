using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class VirusScanMetadata : Metadata
{
    [JsonPropertyName("hasVirus")]
    [JsonPropertyOrder(110)]
    public bool HasVirus { get; set; }

    [JsonPropertyName("virusFound")]
    [JsonPropertyOrder(111)]
    public string? VirusFound { get; set; }

    [JsonPropertyName("virusDefinition")]
    [JsonPropertyOrder(112)]
    public string? VirusDefinition { get; set; }

    public string GetDisplay()
    {
        //return HasVirus ? "☣" : ""; // ""✅"; too noisy
        return HasVirus ? "Has virus" : ""; // ""✅"; too noisy
    }
}