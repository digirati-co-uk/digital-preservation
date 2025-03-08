// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
namespace Preservation.API.Data.Entities;

public class ArchivalGroupEvent
{
    public required DateTime EventDate { get; set; }
    public required Uri ArchivalGroup  { get; set; }
    public Uri? ImportJobResult { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public bool Deleted { get; set; }
}