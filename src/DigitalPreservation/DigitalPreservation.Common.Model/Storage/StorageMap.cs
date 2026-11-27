using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Storage;

public class StorageMap
{
    // v1, v2
    [JsonPropertyName("version")]
    [JsonPropertyOrder(1)]
    public required ObjectVersion Version { get; set; }

    // e.g., S3
    [JsonPropertyName("storageType")]
    [JsonPropertyOrder(2)]
    public required string StorageType { get; set; }

    // e.g., a bucket
    [JsonPropertyName("root")]
    [JsonPropertyOrder(3)]
    public required string Root { get; set; }

    // The key of the object container
    [JsonPropertyName("objectPath")]
    [JsonPropertyOrder(4)]
    public required string ObjectPath { get; set; }

    // What's the string key? - the relative file path.
    // The actual file path (FullPath in the value) may be versioned.
    [JsonPropertyName("files")]
    [JsonPropertyOrder(5)]
    public required Dictionary<string, OriginFile> Files { get; set; }

    // For looking up an S3 path from its digest
    [JsonPropertyName("hashes")]
    [JsonPropertyOrder(6)]
    public required Dictionary<string, string> Hashes { get; set; }

    // v1, v2
    [JsonPropertyName("headVersion")]
    [JsonPropertyOrder(7)]
    public required ObjectVersion HeadVersion { get; set; }

    [JsonPropertyName("allVersions")]
    [JsonPropertyOrder(8)]
    public required ObjectVersion[] AllVersions { get; set; }

    [JsonPropertyName("archivalGroup")]
    [JsonPropertyOrder(8)]
    public required Uri ArchivalGroup { get; set; }

    /// <summary>
    /// Every file that some earlier version had and this version does not, keyed by its relative
    /// path, with where its content was the last time it was there. A rename shows here under its
    /// old path. Lets a consumer that registered files elsewhere (a IIIF builder, issue #261) find
    /// what to remove without knowing which versions it has already processed.
    /// </summary>
    [JsonPropertyName("formerFiles")]
    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, FormerOriginFile>? FormerFiles { get; set; }
}

public class OriginFile
{
    [JsonPropertyName("hash")]
    [JsonPropertyOrder(1)]
    public required string Hash { get; set; }

    [JsonPropertyName("fullPath")]
    [JsonPropertyOrder(2)]
    public required string FullPath { get; set; }

    /// <summary>
    /// The version in which this path last got the content it has now, e.g. <c>v3</c>. Usually the
    /// version in <see cref="FullPath"/>, but not when content returns to bytes stored before: OCFL
    /// stores identical bytes once, so a file that was X in v1, Y in v2 and X again in v3 has a v1
    /// content path and a <c>contentVersion</c> of v3 (issue #261). Not set on a former file.
    /// </summary>
    [JsonPropertyName("contentVersion")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentVersion { get; set; }

    public override string ToString()
    {
        return FullPath;
    }
}

public class FormerOriginFile : OriginFile
{
    /// <summary>The last version the file was in, e.g. <c>v3</c>.</summary>
    [JsonPropertyName("lastVersion")]
    [JsonPropertyOrder(4)]
    public required string LastVersion { get; set; }
}
