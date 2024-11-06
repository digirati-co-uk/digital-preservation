namespace Preservation.Client;

public class PreservationOptions
{
    public const string Preservation = "Preservation";
    
    /// <summary>
    /// Root URI for PreservationAPI
    /// </summary>
    public required Uri Root { get; set; }

    /// <summary>
    /// Timeout, in MS, for requests made to preservation client 
    /// </summary>
    public double TimeoutMs { get; set; } = 100000;
}