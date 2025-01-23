using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Import;

public class ImportJob : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(ImportJob);
    
    [JsonPropertyOrder(3)]
    [JsonPropertyName("originalId")]
    public Uri? OriginalId { get; set; }
    
    /// <summary>
    /// The Deposit that was used to generate this job, and to which it will be sent if executed.
    /// Only applicable when the ImportJob is returned by the Preservation API, not Storage
    /// </summary>
    [JsonPropertyName("deposit")]
    [JsonPropertyOrder(500)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Deposit { get; set; }
    
    /// <summary>
    /// The object in the repository that the job is to be performed on. This object doesn't necessarily exist yet -
    /// this job might be creating it. The value must match the ArchivalGroup of the deposit, so it's technically
    /// redundant, but must be included so that the intent is explicit and self-contained.
    /// </summary>
    [JsonPropertyName("archivalGroup")]
    [JsonPropertyOrder(510)]
    public Uri? ArchivalGroup { get; set; }
    
    /// <summary>
    /// For a new archivalGroup, the dc:title (name) to give the object in the repository.
    /// </summary>
    [JsonPropertyName("archivalGroupName")]
    [JsonPropertyOrder(520)]
    public string? ArchivalGroupName { get; set; }
    
    /// <summary>
    /// Must be explicitly set to true to allow an update of an existing ArchivalGroup
    /// </summary>
    [JsonPropertyName("isUpdate")]
    [JsonPropertyOrder(525)]
    public bool IsUpdate { get; set; }
    
    /// <summary>
    /// A filesystem or S3 path for the directory that will be compared to the archival object
    /// (NB not needed to be set on Presentation API as the Deposit provides it, but will be reported back)
    /// </summary>
    [JsonPropertyName("source")]
    [JsonPropertyOrder(530)]
    public Uri? Source { get; set; }
    
    /// <summary>
    /// Always provided when you ask the API to generate an ImportJob as a diff and the ArchivalGroup already exists.
    /// May be null for a new object
    /// </summary>
    [JsonPropertyOrder(540)]
    public ObjectVersion? SourceVersion { get; set; }  // TODO - name of this thing

    /// <summary>
    /// A list of Container objects to be created within the Archival Group. The id property gives the URI of the
    /// container to be created, whose path must be "within" the Digital Object and must only use characters from the
    /// permitted set. The name property of the container may be any UTF-8 characters, and can be used to preserve an
    /// original directory name.
    /// </summary>
    [JsonPropertyOrder(610)]
    public List<Container> ContainersToAdd { get; set; } = [];

    /// <summary>
    /// A list of Binary objects to be created within the Archival Group from keys in S3. The id property gives the URI
    /// of the binary to be created, whose path must be "within" the Digital Object and must only use characters from
    /// the permitted set. The name property of the Binary may be any UTF-8 characters, and can be used to preserve an
    /// original file name. The location must be an S3 key within the Deposit. The digest is only required if the SHA256
    /// cannot be obtained by the API from METS file information or from S3 metadata. All API-generated jobs will
    /// include this field. 
    /// </summary>
    [JsonPropertyOrder(620)]
    public List<Binary> BinariesToAdd { get; set; } = [];

    /// <summary>
    /// A list of containers to remove. id is the only required property. The Containers must either be already empty,
    /// or only contain Binaries mentioned in the binariesToDelete property of the same ImportJob.
    /// </summary>
    [JsonPropertyOrder(630)]
    public List<Container> ContainersToDelete { get; set; } = [];

    /// <summary>
    /// A list of binaries to remove. id is the only required property.
    /// </summary>
    [JsonPropertyOrder(640)]
    public List<Binary> BinariesToDelete { get; set; } = [];

    /// <summary>
    /// A list of Binary objects to be updated within the Digital object from keys in S3. The id property gives the URI
    /// of the binary to be patched, which must already exist. The name property of the Binary may be any UTF-8
    /// characters, and can be used to preserve an original file name. This may be different from the originally
    /// supplied name. The location must be an S3 key within the Deposit. The digest is only required if the SHA256
    /// cannot be obtained by the API from METS file information or from S3 metadata.
    /// </summary>
    [JsonPropertyOrder(650)]
    public List<Binary> BinariesToPatch { get; set; } = [];
    
    // TODO: (not for demo) - change name (dc:title) of containers and binaries
    /// <summary>
    /// This cannot change the slug (path) but can change the name - i.e., the dc:title
    /// </summary>
    [JsonPropertyOrder(750)]
    public List<Container> ContainersToRename { get; set; } = [];
    
    /// <summary>
    /// This cannot change the slug (path) but can change the name - i.e., the dc:title
    /// </summary>
    [JsonPropertyOrder(760)]
    public List<Binary> BinariesToRename { get; set; } = [];
    
    // (metadata to change?) more general. NOT for Oct demo.

    public List<PreservedResource> ItemsWithInvalidSlugs()
    {
        var items = new List<PreservedResource>();
        items.AddRange(ContainersToAdd.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(ContainersToRename.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(ContainersToDelete.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(BinariesToAdd.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(BinariesToPatch.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(BinariesToRename.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        items.AddRange(BinariesToDelete.Where(container => !PreservedResource.ValidSlug(container.GetSlug())));
        return items;
    }

    public List<Binary> AddedBinariesWithInvalidContentTypes()
    {
        var binaries = new List<Binary>();
        binaries.AddRange(BinariesToAdd.Where(binary => binary.ContentType.IsNullOrWhiteSpace()));
        binaries.AddRange(BinariesToPatch.Where(binary => binary.ContentType.IsNullOrWhiteSpace()));
        // may be not required here
        // binaries.AddRange(BinariesToRename.Where(binary => binary.ContentType.IsNullOrWhiteSpace()));
        return binaries;
    }
}