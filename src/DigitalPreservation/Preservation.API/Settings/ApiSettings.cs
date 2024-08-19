using Storage.Client;

namespace Preservation.API.Settings;

public class ApiSettings
{
    /// <summary>
    /// Root URI for storage-api
    /// </summary>
    public required StorageOptions ApiStorageOptions { get; set; }
}