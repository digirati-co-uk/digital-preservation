using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class VirusScanMetadata : Metadata
{
    [JsonPropertyName("hasVirus")]
    [JsonPropertyOrder(110)]
    public bool HasVirus { get; set; }

    [JsonPropertyName("virusFound")]
    [JsonPropertyOrder(110)]
    public string VirusFound { get; set; }

    public string GetDisplay()
    {
        //return HasVirus ? "☣" : ""; // ""✅"; too noisy
        return HasVirus ? "Has virus" : ""; // ""✅"; too noisy
    }
}