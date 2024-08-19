using Storage.Client;

namespace Preservation.API.Settings;

public class ApiSettings
{
    /// <summary>
    /// Root URI for storage-api
    /// </summary>
    public StorageOptions ApiStorageOptions { get; set; }
}