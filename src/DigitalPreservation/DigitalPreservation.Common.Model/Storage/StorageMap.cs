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
}

public class OriginFile
{
    [JsonPropertyName("hash")]
    [JsonPropertyOrder(1)]
    public required string Hash { get; set; }

    [JsonPropertyName("fullPath")]
    [JsonPropertyOrder(2)]
    public required string FullPath { get; set; }

    public override string ToString()
    {
        return FullPath;
    }
}