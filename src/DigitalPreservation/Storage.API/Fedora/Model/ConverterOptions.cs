namespace Storage.API.Fedora.Model;

public class ConverterOptions
{
    public const string Converter = "Converter";
    
    /// <summary>
    /// The root URI for repository paths
    /// </summary>
    public required Uri RepositoryRoot { get; set; }
    
    /// <summary>
    /// The root URI for people and other API callers
    /// </summary>
    public required Uri AgentRoot { get; set; }
}