using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;
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
            Size = Size
        };
    }
}


