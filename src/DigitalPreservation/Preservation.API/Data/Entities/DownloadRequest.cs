namespace Preservation.API.Data.Entities;

/// <summary>
/// A request to download one preserved file (RFC 011). The row is the record of the request and is
/// never removed; what expires or is deleted is the copy of the file.
/// </summary>
public class DownloadRequest
{
    public required string Id { get; set; }

    /// <summary>The file's path in the repository, under the root.</summary>
    public required string Path { get; set; }

    public required string Name { get; set; }
    public required string Digest { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }

    public required string RequestedBy { get; set; }
    public string? Reason { get; set; }
    public DateTime Created { get; set; }
    public DateTime Expires { get; set; }

    /// <summary>One of DownloadStatus.</summary>
    public required string Status { get; set; }
    public string? Error { get; set; }

    public List<DownloadLinkIssued> LinksIssued { get; set; } = [];
}

/// <summary>One occasion on which a link to a download's copy was handed to someone.</summary>
public class DownloadLinkIssued
{
    public int Id { get; set; }
    public required string DownloadRequestId { get; set; }
    public required string IssuedTo { get; set; }
    public DateTime Issued { get; set; }
}
