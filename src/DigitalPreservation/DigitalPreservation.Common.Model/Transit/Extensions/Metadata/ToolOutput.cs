namespace DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

public class ToolOutput : Metadata
{
    public required string ContentType { get; set; }
    public required string Content { get; set; }
}