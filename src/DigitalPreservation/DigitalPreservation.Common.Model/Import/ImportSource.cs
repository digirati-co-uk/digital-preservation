using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.Import;

public class ImportSource
{
    [JsonPropertyName("source")]
    [JsonPropertyOrder(10)]
    public required Uri Source { get; set; }

    // If the source itself can supply a name: typically mods:title from a METS file
    [JsonPropertyName("name")]
    [JsonPropertyOrder(20)]
    public string? Name => Root.Name;
    
    [JsonPropertyName("root")]
    
    [JsonPropertyOrder(30)]
    public required WorkingDirectory Root { get; set; }

    public Container AsContainer(Uri repositoryUri)
    {
        return Root.ToContainer(repositoryUri, Source);
    }
 
    
}