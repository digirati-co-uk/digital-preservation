using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class MetsExtensions
{
    [JsonPropertyName("href")]
    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; set; }
    
    /// <summary>
    /// The raw ID of the mets:div this resource came from. OPAQUE: never parse, substring or
    /// derive a path from it, and never construct one to look a div up by. The platform mints
    /// these from paths, but the encoding has changed (issue #188) and third-party METS uses
    /// entirely different schemes, so a document can carry several forms at once. Round-trip it
    /// verbatim; get paths from <see cref="Href"/>.
    /// </summary>
    [JsonPropertyName("divId")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DivId { get; set; }

    /// <summary>
    /// The raw ID of the mets:amdSec for this resource. Opaque, on the same terms as
    /// <see cref="DivId"/>.
    /// </summary>
    [JsonPropertyName("admId")]
    [JsonPropertyOrder(102)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdmId { get; set; }
}