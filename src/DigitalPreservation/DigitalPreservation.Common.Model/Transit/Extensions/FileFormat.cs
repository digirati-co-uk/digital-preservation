namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class FileFormat
{
    public required string Name { get; set; } = "(no name set)";
    public required string Key { get; set; } = "(no key set)";
    
    public string? GetDisplay()
    {
        return $"{Key}: {Name}";
    }
}