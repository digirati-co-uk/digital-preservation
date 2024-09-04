namespace Storage.API.Fedora;

public class FedoraOptions
{
    public const string Fedora = "Fedora";
    
    /// <summary>
    /// Root URI for Fedora (including /fcrepo/rest/) path
    /// </summary>
    public required Uri Root { get; set; }
    
    /// <summary>
    /// Admin username for Fedora
    /// </summary>
    public required string AdminUser { get; set; }
    
    /// <summary>
    /// Admin password for Fedora
    /// </summary>
    public required string AdminPassword { get; set; }

    /// <summary>
    /// Timeout, in MS, for requests made to storage client 
    /// </summary>
    public double TimeoutMs { get; set; } = 15000;
}