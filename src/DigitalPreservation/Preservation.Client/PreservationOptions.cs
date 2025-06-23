namespace Preservation.Client;

public class PreservationOptions
{
    public const string Preservation = "Preservation";
    
    /// <summary>
    /// Root URI for PreservationAPI
    /// </summary>
    public required Uri Root { get; set; }

    /// <summary>
    /// Timeout, in MINUTES, for requests made to preservation client 
    public double TimeoutMinutes { get; set; } = 30;
    
    public string? ManifestHost { get; set; }
}