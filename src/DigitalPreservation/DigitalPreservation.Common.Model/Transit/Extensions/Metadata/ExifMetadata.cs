using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.DepositHelpers;

namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ExifMetadata : Metadata
{
    public List<ExifTag>? RawToolOutput { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public double BitRate { get; set; }
    public double Duration { get; set; }
}