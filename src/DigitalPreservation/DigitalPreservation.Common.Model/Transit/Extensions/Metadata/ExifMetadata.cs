using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : Metadata
{
    public Dictionary<string, string>? RawToolOutput { get; set; }
}