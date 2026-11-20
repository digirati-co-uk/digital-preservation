namespace Storage.API.Features.Downloads;

public class DownloadOptions
{
    public const string Downloads = "Downloads";

    /// <summary>
    /// The bucket that copies for download are placed in. Only the Storage API has any access to
    /// it. With no bucket configured, the Storage API makes no copies.
    /// </summary>
    public string? Bucket { get; set; }

    /// <summary>
    /// How long a signed link to a copy works. Short, because a link is signed each time someone
    /// asks for the file; a download that has started carries on after its link expires.
    /// </summary>
    public int LinkLifetimeSeconds { get; set; } = 120;
}
