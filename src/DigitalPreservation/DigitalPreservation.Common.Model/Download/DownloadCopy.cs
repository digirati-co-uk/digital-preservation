using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Download;

/// <summary>
/// What the Preservation API asks the Storage API for: a copy of one preserved file, placed in the
/// downloads bucket. The file is named by its digest, which is how the repository finds it.
/// </summary>
public class DownloadCopy
{
    /// <summary>The download request's id, minted by the Preservation API.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("archivalGroup")]
    public required Uri ArchivalGroup { get; set; }

    /// <summary>The file's SHA-256, as the Archival Group records it.</summary>
    [JsonPropertyName("digest")]
    public required string Digest { get; set; }

    /// <summary>The file name the person downloading will receive.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }
}

/// <summary>Whether a requested copy is in the downloads bucket yet.</summary>
public class DownloadCopyStatus
{
    [JsonPropertyName("ready")]
    public required bool Ready { get; set; }
}

/// <summary>A link to a copy in the downloads bucket, signed by S3 and valid for a short time.</summary>
public class DownloadLink
{
    [JsonPropertyName("url")]
    public required Uri Url { get; set; }

    [JsonPropertyName("expires")]
    public required DateTime Expires { get; set; }
}
