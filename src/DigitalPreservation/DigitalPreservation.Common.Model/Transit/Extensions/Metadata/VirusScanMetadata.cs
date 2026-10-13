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

    /// <summary>
    /// True when ClamAV skipped this file because it exceeded a configured size limit (issue #371)
    /// - the scan never ran, so this is neither "clean" (<see cref="HasVirus"/> false with a real
    /// scan behind it) nor "infected". Mutually exclusive with <see cref="HasVirus"/>: a file this
    /// is true for never has HasVirus set true, however ClamAV phrased the skip.
    /// </summary>
    [JsonPropertyName("notScanned")]
    [JsonPropertyOrder(113)]
    public bool NotScanned { get; set; }

    public string GetDisplay()
    {
        if (NotScanned) return "Not scanned: exceeds size limit";
        //return HasVirus ? "☣" : ""; // ""✅"; too noisy
        return HasVirus ? "Has virus" : ""; // ""✅"; too noisy
    }
}