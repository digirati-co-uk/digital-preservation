using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace;

public class MetadataReader : IMetadataReader
{
    private readonly IStorage storage;
    private readonly Uri rootUri;

    public static async Task<MetadataReader> Create(IStorage storage, Uri rootUri)
    {
        var reader = new MetadataReader(storage, rootUri);
        await reader.Initialize();
        return reader;
    }

    private MetadataReader(IStorage storage, Uri rootUri)
    {
        this.storage = storage;
        this.rootUri = rootUri;
    }

    private async Task Initialize()
    {
        bool isBagItLayout = false;
        // use storage to read and parse all sources of metadata
        var bagitStreamResult = await storage.GetStream(rootUri.AppendSlug("bagit.txt"));
        if (bagitStreamResult.Success)
        {
            isBagItLayout = true;
        }
        // It still might be a bagit layout but without any root bagit files yet
        
        // read bagit sha256 manifest
        // read any other bagit manifest what the hell
        
        // for the following we look in data/{path} as well as {path}
        
        // find {}/metadata/siegfried/siegfried.yml
        // find {}/metadata/siegfried/siegfried.yaml
        // find {}/metadata/siegfried/siegfried.csv
        
        // find {}/metadata/brunnhilde/siegfried.csv
        // find {}/metadata/brunnhilde/logs/viruscheck-log.txt
        
        // find {}/metadata/brunnhilde/report.html and surface differently ? Maybe not here
        
        // when parsing files with paths, find common origin and look for objects/
        // be lenient to absolute and relative paths
        
    }
    
    public void Decorate(WorkingBase workingBase)
    {
        // look up in the built sources any metadata you have for this file or folder
        
    }
}