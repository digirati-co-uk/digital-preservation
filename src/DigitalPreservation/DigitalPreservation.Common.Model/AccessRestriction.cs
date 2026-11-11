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
}

