namespace DigitalPreservation.Common.Model.ToolOutput.Siegfried;

public class File
{
    public string? Filename { get; set; }
    public long? Filesize { get; set; }
    public DateTime? Modified { get; set; }
    public List<Error>? Errors { get; set; } = [];
    public string? Sha256 { get; set; }
    public List<Match>? Matches { get; set; } = [];
}