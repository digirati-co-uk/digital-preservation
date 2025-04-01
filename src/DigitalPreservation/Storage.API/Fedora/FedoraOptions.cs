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
    /// Timeout, in MINUTES, for requests made to Fedora
    /// </summary>
    public double TimeoutMinutes { get; set; } = 10;

    // This feels like a storage API setting not a Fedora setting, but it's used by Fedora client...
    public bool RequireDigestOnBinary { get; set; } = true;
    
    /// <summary>
    /// Fedora's repository bucket (not the deposits)
    /// </summary>
    public required string Bucket { get; set; }

    public required string OcflS3Prefix { get; set; } = String.Empty;
}