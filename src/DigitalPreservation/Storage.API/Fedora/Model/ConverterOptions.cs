namespace Storage.API.Fedora.Model;

public class ConverterOptions
{
    public const string Converter = "Converter";
    
    public required Uri StorageRoot { get; set; }
}