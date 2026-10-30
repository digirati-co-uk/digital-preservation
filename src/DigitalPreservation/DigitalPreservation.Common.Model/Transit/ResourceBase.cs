using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Common.Model.Transit;

public abstract class ResourceBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public abstract string Type { get; set; }

    [JsonPropertyName("name")]
    [JsonPropertyOrder(2)]
    public string? Name { get; set; }

    // METS-specific information
    [JsonPropertyName("metsExtensions")]
    [JsonPropertyOrder(100)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetsExtensions? MetsExtensions { get; set; }

    [JsonPropertyName("accessRestrictions")]
    [JsonPropertyOrder(5)]
    public List<string>? AccessRestrictions { get; set; }

    [JsonPropertyName("effectiveAccessRestrictions")]
    [JsonPropertyOrder(6)]
    public List<string> EffectiveAccessRestrictions { get; set; } = [];

    [JsonPropertyName("rightsStatement")]
    [JsonPropertyOrder(10)]
    public Uri? RightsStatement { get; set; }

    [JsonPropertyName("effectiveRightsStatement")]
    [JsonPropertyOrder(11)]

    public Uri? EffectiveRightsStatement { get; set; }

    /// <summary>
    /// True when the resource has an explicit (but empty/non-URI) rights statement in METS —
    /// i.e. a <c>use and reproduction</c> element is present but asserts no rights URI. This is
    /// distinct from having no rights element at all: a suppressed resource deliberately STOPS
    /// inheriting rights from its ancestors and has a null <see cref="EffectiveRightsStatement"/>,
    /// whereas a resource with no element inherits its parent's effective rights.
    /// </summary>
    [JsonPropertyName("rightsStatementSuppressed")]
    [JsonPropertyOrder(12)]
    public bool RightsStatementSuppressed { get; set; }

    /// <summary>
    /// Every <c>mods:accessCondition</c> on this resource of a type OTHER than the two chosen ones
    /// (<c>restriction on access</c> and <c>use and reproduction</c>), keyed by <c>@type</c>, first
    /// value per type. The platform writes its configured derived conditions here - for Leeds the
    /// access tier (issue #433) - but the parser cannot know which types a configuration derives,
    /// so a third-party document's other typed conditions appear here too. Read-only through the
    /// API: derived values are recomputed from the chosen ones on every write.
    /// </summary>
    [JsonPropertyName("derivedAccessConditions")]
    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? DerivedAccessConditions { get; set; }

    /// <summary>
    /// <see cref="DerivedAccessConditions"/> after inheritance, per type: a type set here overrides
    /// the ancestor's value for that type and inherits the rest.
    /// </summary>
    [JsonPropertyName("effectiveDerivedAccessConditions")]
    [JsonPropertyOrder(14)]
    public Dictionary<string, string> EffectiveDerivedAccessConditions { get; set; } = [];

    [JsonPropertyName("recordInfo")]
    [JsonPropertyOrder(15)]
    public RecordInfo? RecordInfo { get; set; }

    [JsonPropertyName("effectiveRecordInfo")]
    [JsonPropertyOrder(16)]
    public RecordInfo? EffectiveRecordInfo { get; set; }
}