using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public class AccessRestriction
{
    public static List<AccessRestriction> All { get; }
    
    static AccessRestriction()
    {
        All =
        [
            new AccessRestriction { Label = "Open", Value = "Open" },
            new AccessRestriction { Label = "Restricted", Value = "Restricted" },
            new AccessRestriction { Label = "Staff", Value = "Staff" },
            new AccessRestriction { Label = "Closed", Value = "Closed" }
        ];
    }
    
    /// <summary>What a person sees. Named explicitly so the profile round-trips through a default
    /// (case-sensitive) deserialiser as well as the web one (review of #434).</summary>
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>What is written to METS and returned in <c>accessRestrictions</c>.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>When to use it, for a person choosing (issue #433); optional.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Identifiers for the same value in other systems, by scheme (issue #433) - for Leeds, the
    /// EMu Rights SPRIG and IRN of a tier - so that a flow starting from those systems can map
    /// them to this value. Optional.
    /// </summary>
    [JsonPropertyName("externalIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? ExternalIds { get; set; }
}

