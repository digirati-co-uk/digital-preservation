using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Storage.Ocfl;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Ocfl;

public class OcflS3StorageMapper(
    IOptions<ConverterOptions> converterOptions,
    ILogger<OcflS3StorageMapper> logger,
    Converters converters,
    IAmazonS3 awsS3Client,
    IOptions<FedoraOptions> fedoraOptions) : IStorageMapper
{
    private readonly FedoraOptions fedora = fedoraOptions.Value;

    public async Task<StorageMap> GetStorageMap(Uri archivalGroupUri, string? version = null)
    {
        logger.LogInformation("Getting storage map for " + archivalGroupUri + " version " + version);
        var agOrigin = GetArchivalGroupOrigin(archivalGroupUri);
        logger.LogInformation("agOrigin={agOrigin}", agOrigin);
        Inventory? inventory = await GetInventory(agOrigin);
        var inventoryVersions = inventory!.Versions
            .Select(kvp => new ObjectVersion
            {
                OcflVersion = kvp.Key,
                MementoDateTime = kvp.Value.Created,
                MementoTimestamp = kvp.Value.Created.ToMementoTimestamp(),
            })
           .OrderBy(o => o.MementoDateTime)
           .ToList();

        if (version == null)
        {
            // Use the latest version
            version = inventory.Head;
        }

        // Allow the supplied string to be either ocfl vX or memento timestamp (they cannot overlap!)
        ObjectVersion objectVersion = inventoryVersions.Single(v => v.OcflVersion == version || v.MementoTimestamp == version);
        ObjectVersion headObjectVersion = inventoryVersions.Single(v => v.OcflVersion == inventory.Head);

        var mapFiles = new Dictionary<string, OriginFile>();
        var hashes = new Dictionary<string, string>();
        var ocflVersion = inventory.Versions[objectVersion.OcflVersion!];
        foreach (var kvp in ocflVersion.State)
        {
            var files = kvp.Value.Where(f => !IsFedoraMetadata(f)).ToList();
            if (files.Count > 0)
            {
                var hash = kvp.Key;
                var actualPath = inventory.Manifest[hash][0];// I don't think there'll ever be more than one entry in a Fedora instance - see https://ocfl.io/1.1/spec/#manifest 
                var originFile = new OriginFile
                {
                    Hash = hash,
                    FullPath = actualPath
                };
                hashes[hash] = actualPath;
                foreach (var file in files)
                {
                    mapFiles[file] = originFile;
                }
            }
        }

        // Validate that the OCFL layout thinks this is an Archival Group
        var rootInfoKey = $"{agOrigin}/{objectVersion.OcflVersion}/content/.fcrepo/fcr-root.json";
        var rootInfoReq = new GetObjectRequest { BucketName = fedora.Bucket, Key = rootInfoKey };
        var rootInfoinvResp = await awsS3Client.GetObjectAsync(rootInfoReq);

        bool? archivalGroup = null;
        bool? objectRoot = null;
        bool? deleted = null;
        using (JsonDocument jDoc = JsonDocument.Parse(rootInfoinvResp.ResponseStream))
        {
            if (jDoc.RootElement.TryGetProperty("archivalGroup", out JsonElement jArchivalGroup))
            {
                archivalGroup = jArchivalGroup.GetBoolean();
            }
            if (jDoc.RootElement.TryGetProperty("objectRoot", out JsonElement jObjectRoot))
            {
                objectRoot = jObjectRoot.GetBoolean();
            }
            if (jDoc.RootElement.TryGetProperty("deleted", out JsonElement jDeleted))
            {
                deleted = jDeleted.GetBoolean();
            }
        }

        if (
            archivalGroup.HasValue && archivalGroup == true &&
            objectRoot.HasValue && objectRoot == true &&
            deleted.HasValue && deleted == false)
        {
            return new StorageMap()
            {
                ArchivalGroup = archivalGroupUri,
                Version = objectVersion,
                HeadVersion = headObjectVersion,
                StorageType = StorageTypes.S3,
                Root = fedora.Bucket,
                ObjectPath = agOrigin!,
                AllVersions = inventoryVersions.ToArray(),
                Files = mapFiles,
                Hashes = hashes
            };
        }
        else
        {
            throw new InvalidOperationException("Not an archival object");
        }

    }

    public async Task<Inventory?> GetInventory(Uri archivalGroupUri)
    {
        var agOrigin = GetArchivalGroupOrigin(archivalGroupUri);
        Inventory? inventory = await GetInventory(agOrigin);
        return inventory;
    }
    
    private async Task<Inventory?> GetInventory(string? agOrigin)
    {
        logger.LogInformation("About to fetch inventory from bucket {bucket} and key {key}", fedora.Bucket, $"{agOrigin}/inventory.json");
        var invReq = new GetObjectRequest { BucketName = fedora.Bucket, Key = $"{agOrigin}/inventory.json" };
        var invResp = await awsS3Client.GetObjectAsync(invReq);
        var inventory = JsonSerializer.Deserialize<Inventory>(invResp.ResponseStream);
        return inventory;
    }
    
    public string? GetArchivalGroupOrigin(Uri archivalGroupUri)
    {
        logger.LogInformation("GetArchivalGroupOrigin for " + archivalGroupUri);
        var idPart = converters.GetResourcePathPart(archivalGroupUri);
        logger.LogInformation("converters.GetResourcePathPart(archivalGroupUri) => " + idPart);
        
        if (idPart == null)
        {
            return null;
        }
        return RepositoryPath.RelativeToRoot(fedora.OcflS3Prefix, idPart);
    }
    
    private bool IsFedoraMetadata(string filepath)
    {
        if (
            filepath.StartsWith(".fcrepo/")       ||
            filepath.EndsWith("fcr-container.nt") ||
            filepath.EndsWith("~fcr-desc.nt")     || 
            filepath.EndsWith("~fcr-acl.nt")
        )
        {
            return true;
        }
        return false;
    }
}