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
    /// True when ClamAV skipped this file because it hit one of the limits AlertExceedsMax reports
    /// (issue #371) - not just MaxFileSize/MaxScanSize: MaxRecursion, MaxFiles and MaxScanTime
    /// report the same way (<see cref="VirusFound"/> carries the specific
    /// Heuristics.Limits.Exceeded.* reason). The scan never ran to completion, so this is neither
    /// "clean" (<see cref="HasVirus"/> false with a real scan behind it) nor "infected". Mutually
    /// exclusive with <see cref="HasVirus"/>: a file this is true for never has HasVirus set true,
    /// however ClamAV phrased the skip.
    /// </summary>
    [JsonPropertyName("notScanned")]
    [JsonPropertyOrder(113)]
    public bool NotScanned { get; set; }

    public string GetDisplay()
    {
        // Not "exceeds size limit": AlertExceedsMax also fires for MaxScanTime/MaxRecursion/MaxFiles,
        // none of which are a size. VirusFound (shown alongside, e.g. in the file table's tooltip)
        // carries the specific reason.
        if (NotScanned) return "Not fully scanned: exceeded a ClamAV limit";
        //return HasVirus ? "☣" : ""; // ""✅"; too noisy
        return HasVirus ? "Has virus" : ""; // ""✅"; too noisy
    }
}