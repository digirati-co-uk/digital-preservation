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
    public List<string> AccessRestrictions { get; set; } = [];

    [JsonPropertyName("effectiveAccessRestrictions")]
    [JsonPropertyOrder(6)]
    public List<string> EffectiveAccessRestrictions { get; set; } = [];

    [JsonPropertyName("rightsStatement")]
    [JsonPropertyOrder(10)]
    public Uri? RightsStatement { get; set; }

    [JsonPropertyName("effectiveRightsStatement")]
    [JsonPropertyOrder(11)]
    public Uri? EffectiveRightsStatement { get; set; }
}