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
    /// Keep the new version this job creates out of the published Activity Stream. For maintenance
    /// that changes how the object is recorded rather than what it holds - the METS ID migration of
    /// issue #188 step 3 is the reason this exists.
    /// </summary>
    /// <remarks>
    /// The stream is a IIIF Change Discovery feed: its readers, iiif-builder among them, treat an
    /// entry as "this object changed, rebuild what you derived from it". A migration that renames
    /// identifiers inside the METS produces nothing for them to rebuild, so an entry would be both
    /// wasted work and a misleading account of the object's history.
    /// <para>
    /// It suppresses the Activity Stream ENTRY, not the record: Preservation API still stores the
    /// event, and OCFL still holds the new version with everything that made it. Nothing about the
    /// object's history is lost - it just isn't announced as a change.
    /// </para>
    /// </remarks>
    [JsonPropertyName("suppressActivityStreamEvent")]
    [JsonPropertyOrder(526)]
    public bool SuppressActivityStreamEvent { get; set; }
    
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
    [JsonPropertyName("sourceVersion")]
    [JsonPropertyOrder(540)]
    public ObjectVersion? SourceVersion { get; set; }

    /// <summary>
    /// A list of Container objects to be created within the Archival Group. The id property gives the URI of the
    /// container to be created, whose path must be "within" the Digital Object and must only use characters from the
    /// permitted set. The name property of the container may be any UTF-8 characters, and can be used to preserve an
    /// original directory name.
    /// </summary>
    [JsonPropertyName("containersToAdd")]
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
    [JsonPropertyName("binariesToAdd")]
    [JsonPropertyOrder(620)]
    public List<Binary> BinariesToAdd { get; set; } = [];

    /// <summary>
    /// A list of containers to remove. id is the only required property. The Containers must either be already empty,
    /// or only contain Binaries mentioned in the binariesToDelete property of the same ImportJob.
    /// </summary>
    [JsonPropertyName("containersToDelete")]
    [JsonPropertyOrder(630)]
    public List<Container> ContainersToDelete { get; set; } = [];

    /// <summary>
    /// A list of binaries to remove. id is the only required property.
    /// </summary>
    [JsonPropertyName("binariesToDelete")]
    [JsonPropertyOrder(640)]
    public List<Binary> BinariesToDelete { get; set; } = [];

    /// <summary>
    /// A list of Binary objects to be updated within the Digital object from keys in S3. The id property gives the URI
    /// of the binary to be patched, which must already exist. The name property of the Binary may be any UTF-8
    /// characters, and can be used to preserve an original file name. This may be different from the originally
    /// supplied name. The location must be an S3 key within the Deposit. The digest is only required if the SHA256
    /// cannot be obtained by the API from METS file information or from S3 metadata.
    /// </summary>
    [JsonPropertyName("binariesToPatch")]
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

    public (List<PreservedResource>, string?) ItemsWithInvalidSlugs()
    {
        var itemsWithInvalidSlugs = new List<PreservedResource>();
        var invalidSlugMessages = new List<string>();
        AppendInvalidSlugMessage(ContainersToAdd, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(ContainersToRename, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(ContainersToDelete, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(BinariesToAdd, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(BinariesToPatch, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(BinariesToRename, itemsWithInvalidSlugs, invalidSlugMessages);
        AppendInvalidSlugMessage(BinariesToDelete, itemsWithInvalidSlugs, invalidSlugMessages);
        string? msg = null;
        if (invalidSlugMessages.Count > 0)
        {
            msg = string.Join("; ", invalidSlugMessages);
        }
        return (itemsWithInvalidSlugs, msg);
    }

    private static void AppendInvalidSlugMessage(IEnumerable<PreservedResource> itemsToTest, List<PreservedResource> itemsWithInvalidSlugs, List<string> invalidSlugMessages)
    {
        foreach (var item in itemsToTest)
        {
            if (!PreservedResource.ValidSlug(item.GetSlug(), out var msg))
            {
                itemsWithInvalidSlugs.Add(item);
                invalidSlugMessages.Add(msg!);   
            }
        }
    }

    public List<Binary> AddedBinariesWithInvalidContentTypes()
    {
        var binaries = new List<Binary>();
        binaries.AddRange(BinariesToAdd.Where(binary => binary.ContentType.IsNullOrWhiteSpace()));
        binaries.AddRange(BinariesToPatch.Where(binary => binary.ContentType.IsNullOrWhiteSpace()));
        return binaries;
    }
}