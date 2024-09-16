namespace Storage.Client;

public class StorageOptions
{
    public const string Storage = "Storage";
    
    /// <summary>
    /// Root URI for StorageAPI
    /// </summary>
    public required Uri Root { get; set; }

    /// <summary>
    /// Timeout, in MS, for requests made to storage client 
    /// </summary>
    public double TimeoutMs { get; set; } = 15000;
    
}