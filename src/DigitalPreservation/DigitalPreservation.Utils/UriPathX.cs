namespace DigitalPreservation.Utils;

/// <summary>
/// The one rule for whether a single segment of a caller-supplied URI path could move the path
/// somewhere other than where it appears to point. Shared by the Storage API's repository-path
/// validation and the Preservation API's media-path validation, so that a bypass fixed in one is
/// fixed in both.
/// </summary>
/// <remarks>
/// Not to be confused with <see cref="PathX.IsUnderRoot"/>, which judges a local filesystem path
/// after it has been resolved; this judges a URI path segment before anything is built from it.
/// </remarks>
public static class UriPathX
{
    /// <summary>
    /// True if the segment, as given or after one more percent-decoding, is a dot segment, contains a
    /// path separator of either kind, or contains a NUL. Route values reach controllers decoded once,
    /// so <c>%2e%2e</c> is what a doubly-encoded <c>..</c> looks like here, and <see cref="Uri"/> will
    /// canonicalise it as <c>..</c> when a URI is built from it.
    /// </summary>
    public static bool IsTraversalSegment(string segment)
    {
        if (segment is "." or "..") return true;
        if (segment.Contains('\\') || segment.Contains('\0')) return true;
        var decoded = Uri.UnescapeDataString(segment);
        return decoded is "." or ".."
               || decoded.Contains('/') || decoded.Contains('\\') || decoded.Contains('\0');
    }
}
