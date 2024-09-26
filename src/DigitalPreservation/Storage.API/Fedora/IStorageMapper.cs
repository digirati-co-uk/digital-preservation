using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Storage.Ocfl;

namespace Storage.API.Fedora;

public interface IStorageMapper
{
    Task<StorageMap> GetStorageMap(Uri archivalGroupUri, string? version = null);

    Task<Inventory?> GetInventory(Uri archivalGroupUri);

    Uri GetArchivalGroupOrigin(Uri archivalGroupUri); // (was string?)
}