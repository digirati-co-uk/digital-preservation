using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit;

/// <summary>
/// A file's extent as a person stated it, not as a tool measured it (issue #508): for a file the
/// pipeline couldn't measure, or wasn't run on. Recorded in the file's MODS, as
/// <c>mods:physicalDescription/mods:extent</c> with <c>supplied="yes"</c>, so it is never confused
/// with a measured value, which lives in the PREMIS technical metadata. A measured value always
/// wins: see <see cref="WorkingFile.GetEffectiveExtent"/>.
/// </summary>
public record EnteredExtent
{
    /// <summary>The duration in seconds.</summary>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Duration { get; init; }

    [JsonPropertyName("pixelWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PixelWidth { get; init; }

    [JsonPropertyName("pixelHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PixelHeight { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Duration is null && PixelWidth is null && PixelHeight is null;

    /// <summary>The MODS extent units for each value.</summary>
    public const string SecondsUnit = "seconds";
    public const string WidthUnit = "pixels wide";
    public const string HeightUnit = "pixels high";
}

/// <summary>
/// The extent to use for a file: each value measured if there is one, otherwise as entered, with
/// whether it was entered by hand.
/// </summary>
public record EffectiveExtent(
    double? Duration, int? PixelWidth, int? PixelHeight,
    bool DurationEntered, bool PixelWidthEntered, bool PixelHeightEntered);
