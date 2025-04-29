using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;
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

    public T? GetAggregatedMetadata<T>(string? preferredSource = null) where T : Metadata
    {
        // TODO - how to spread this out to each specific class, so this doesn't know about implementations of Metadata
        var metadata = Metadata.OfType<T>().ToList();
        if (metadata.Count == 0)
        {
            return null;
        }
        string? digest = null;
        var digests = metadata
            .OfType<IDigestMetadata>()
            .Where(m => m.Digest.HasText())
            .Select(m => m.Digest!)
            .ToList();
        if (digests.Count > 0)
        {
            if (digests.All(x => x == digests.First()))
            {
                digest = digests.First();
            }
        }
        else
        {
            
        }
        
        
        switch (typeof(T).Name)
        {
            case nameof(FileFormatMetadata):

                break;
        }

        return null;

    }
    
    
    
}


