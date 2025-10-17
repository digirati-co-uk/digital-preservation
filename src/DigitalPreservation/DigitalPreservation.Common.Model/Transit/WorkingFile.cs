using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class WorkingFile : WorkingBase
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(WorkingFile); 
    
    [JsonPropertyName("contentType")]
    [JsonPropertyOrder(14)]
    public required string ContentType { get; set; }

    [JsonPropertyName("digest")]
    [JsonPropertyOrder(15)]
    public string? Digest { get; set; }
    
    [JsonPropertyName("size")]
    [JsonPropertyOrder(16)]
    public long? Size { get; set; }

    public WorkingFile ToRootLayout()
    {
        if (!LocalPath.StartsWith($"{FolderNames.BagItData}/"))
        {
            return this;
        }

        return new WorkingFile
        {
            LocalPath = LocalPath.RemoveStart($"{FolderNames.BagItData}/")!,
            MetsExtensions = MetsExtensions,
            Modified = Modified,
            Name = Name,
            ContentType = ContentType,
            Digest = Digest,
            Size = Size,
            Metadata = Metadata
        };
    }

    // TODO - how to spread this out to each specific class, so this doesn't know about implementations of Metadata
    /// <summary>
    /// Create a single FileFormatMetadata object from potentially more than one source (eg two different tools that ran)
    /// </summary>
    /// <returns></returns>
    public FileFormatMetadata? GetFileFormatMetadata()
    {
        var fileFormatMetadata = Metadata
            .OfType<FileFormatMetadata>()
            .ToList();

        var digestMetadata = GetDigestMetadata();

        if (fileFormatMetadata.Count == 0 && FolderNames.IsMetadata(LocalPath))
        {
            // These have not been analysed by file format pipelines
            var syntheticMetadata = new FileFormatMetadata
            {
                Source = "Synthetic",
                Timestamp = Modified,
                Digest = digestMetadata?.Digest ?? Digest,
                Size = Size,
                ContentType = ContentType
            };
            if (ContentType == "application/octet-stream" && MimeTypes.TryGetMimeType(LocalPath.GetSlug(), out var foundMimeType))
            {
                syntheticMetadata.ContentType = foundMimeType;
            }

            return syntheticMetadata;
        }

        if (fileFormatMetadata.Count == 0 && !FolderNames.IsMetadata(LocalPath))
        {
            var objectFileFormatMetadata = new FileFormatMetadata
            {
                Source = "Mets",
                ContentType = ContentType,
                Digest = digestMetadata?.Digest ?? Digest,
                Size = Size,
                OriginalName = LocalPath, // workingFile.LocalPath
                StorageLocation = null // storageLocation
            };

            if (ContentType == "application/octet-stream" && MimeTypes.TryGetMimeType(LocalPath.GetSlug(), out var foundMimeType))
            {
                objectFileFormatMetadata.ContentType = foundMimeType;
            }

            return objectFileFormatMetadata;
        }

        if (fileFormatMetadata.Count == 1)
        {
            return fileFormatMetadata.SingleOrDefault();
        }

        var pronomKeys = fileFormatMetadata
                .Where(m => m.PronomKey.HasText())
                .Select(m => m.PronomKey!)
                .ToList();
        var contentTypes = fileFormatMetadata
            .Where(m => m.ContentType.HasText())
            .Select(m => m.ContentType!)
            .ToList();
        var size = fileFormatMetadata
            .Where(m => m is { Size: > 0 })
            .Select(m => m.Size!)
            .ToList();
        var formatNames = fileFormatMetadata
            .Where(m => m.FormatName.HasText())
            .Select(m => m.FormatName!)
            .ToList();
        var originalNames = fileFormatMetadata
            .Where(m => m.OriginalName.HasText())
            .Select(m => m.OriginalName!)
            .ToList();
        var storageLocations = fileFormatMetadata
            .Where(m => m.StorageLocation != null)
            .Select(m => m.StorageLocation!)
            .ToList();
        var digests = fileFormatMetadata
            .Where(m => m.Digest.HasText())
            .Select(m => m.Digest!)
            .ToList();

        // Should we require that ALL of these agree?
        // Pronom keys should definitely agree.

        // In practice, as Brunnhilde is using Siegfried, they should always agree.
        // (for now - we may have other sources later)
        if (pronomKeys.Count > 0)
        {
            if (pronomKeys.All(x => x == pronomKeys.First()))
            {
                return new FileFormatMetadata
                {
                    Digest = digests.First(), // check the digests metadata for mismatch
                    PronomKey = pronomKeys.First(),
                    ContentType = contentTypes.FirstOrDefault(),
                    Size = size.First(),
                    FormatName = formatNames.First(),
                    OriginalName = originalNames.FirstOrDefault(),
                    StorageLocation = storageLocations.FirstOrDefault(),
                    Source = string.Join(',', fileFormatMetadata.Select(m => m.Source)),
                    Timestamp = fileFormatMetadata.Select(m => m.Timestamp).Max()
                };
            }
        }
        
        // There is only one, or none
        return fileFormatMetadata.SingleOrDefault();
    }



    public DigestMetadata? GetDigestMetadata()
    {
        var digestMetadata = Metadata
            .OfType<IDigestMetadata>()
            .Where(m => m.Digest.HasText())
            .ToList();
        var digests = digestMetadata
            .Select(m => m.Digest!)
            .ToList();
        if (digests.Count == 0)
        {
            return null;
        }
        if (digests.All(x => x == digests.First()))
        {
            return new DigestMetadata
            {
                Digest = digests.First(),
                Source = string.Join(',', digestMetadata.Select(m => m.Source)),
                Timestamp = digestMetadata.Select(m => m.Timestamp).Max()
            };
        }
        // What to do if we have MISMATCHED digests?
        // Going to throw an exception for now but come back to this
        throw new MetadataException($"Digests for {LocalPath} are not all the same");
    }

    public VirusScanMetadata? GetVirusScanMetadata()
    {
        return Metadata.OfType<VirusScanMetadata>().SingleOrDefault();
    }
}


