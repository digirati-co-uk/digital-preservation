// ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
namespace Storage.API.Data.Entities;

public class ImportJob
{
    public required string Id { get; set; }
    
    public required Uri ArchivalGroup { get; set; }
    
    public required string ImportJobJson { get; set; }
    
    public string? ImportJobResultJson { get; set; }

    public bool Active { get; set; } = true;
}