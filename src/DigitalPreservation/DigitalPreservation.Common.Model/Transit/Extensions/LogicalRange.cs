using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

/// <summary>
/// Logical resources in METS
/// </summary>
public class LogicalRange : ResourceBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    public LogicalRange[]? Ranges { get; set; }
    public FilePointer[]? Files { get; set; }
    public override required string Type { get; set; }
}

public class FilePointer
{
    public required string LocalPath { get; set; } // not WorkingFile, caller can look it up
    public Rectangle? RectRegion { get; set; }
    public long? BeginTime { get; set; }
    public long? EndTime { get; set; }
}

public class Rectangle
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
}