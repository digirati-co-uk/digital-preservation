namespace Storage.API.Data.Entities;

public class ExportResult
{
    public required string Id { get; set; }
    
    public required Uri ArchivalGroup { get; set; }
    
    public required Uri Destination { get; set; }
    
    public DateTime? DateBegun { get; set; }
    
    public DateTime? DateFinished { get; set; }
    
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? ExportResultJson { get; set; }
}