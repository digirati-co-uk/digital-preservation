using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : Metadata
{
    public Dictionary<string, string>? RawToolOutput { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double BitRate { get; set; }
    public double Duration { get; set; }
}