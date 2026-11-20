using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Download;

/// <summary>What a caller sends to ask for a preserved file to be made available for download.</summary>
public class NewDownload
{
    /// <summary>The file's path in the repository, e.g. <c>accessions/my-group/objects/photo.tiff</c>.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>Why the file is wanted.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// A request to download one preserved file, and the record of it: who asked, for what, when and
/// why. The file is copied to a downloads area, where it stays until the request expires or is
/// deleted. While the copy is there, a short-lived link to it can be had as often as needed.
/// </summary>
public class Download
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>The file's path in the repository.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>The file name the person downloading receives.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The file's SHA-256, to check the downloaded file against.</summary>
    [JsonPropertyName("digest")]
    public required string Digest { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("requestedBy")]
    public required string RequestedBy { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    /// <summary>When the copy is removed, if nobody deletes it sooner.</summary>
    [JsonPropertyName("expires")]
    public DateTime Expires { get; set; }

    /// <summary>One of <see cref="DownloadStatus"/>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>What went wrong, when the status is <see cref="DownloadStatus.Failed"/>.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>How many times a link to the copy has been handed out.</summary>
    [JsonPropertyName("linksIssued")]
    public int LinksIssued { get; set; }
}

public static class DownloadStatus
{
    /// <summary>The copy is being made.</summary>
    public const string Copying = "copying";

    /// <summary>The copy is there, and links to it can be had.</summary>
    public const string Ready = "ready";

    /// <summary>The copy could not be made.</summary>
    public const string Failed = "failed";

    /// <summary>The copy was removed when its time ran out.</summary>
    public const string Expired = "expired";

    /// <summary>Someone removed the copy before its time ran out.</summary>
    public const string Deleted = "deleted";
}
