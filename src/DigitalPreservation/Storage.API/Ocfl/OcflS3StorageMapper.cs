using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Storage.Ocfl;
using Storage.API.Fedora;

namespace Storage.API.Ocfl;

public class OcflS3StorageMapper : IStorageMapper
{
    public Task<StorageMap> GetStorageMap(Uri archivalGroupUri, string? version = null)
    {
        throw new NotImplementedException();
    }

    public Task<Inventory?> GetInventory(Uri archivalGroupUri)
    {
        throw new NotImplementedException();
    }

    public Uri GetArchivalGroupOrigin(Uri archivalGroupUri)
    {
        throw new NotImplementedException();
    }
}