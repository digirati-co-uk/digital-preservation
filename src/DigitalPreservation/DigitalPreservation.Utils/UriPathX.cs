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
    /// True if the segment, as given or after one more percent-decoding, is a dot segment; contains a
    /// path separator of either kind or a NUL; or contains a character that would end or redirect the
    /// path once a URI is built from it (<c>#</c> starts a fragment, <c>?</c> a query, <c>:</c> a scheme
    /// or a Fedora-reserved <c>fcr:</c> name). Route values reach controllers decoded once, so
    /// <c>%2e%2e</c> is what a doubly-encoded <c>..</c> looks like here, and <see cref="Uri"/> will
    /// canonicalise it as <c>..</c> when a URI is built from it. None of these characters is a legal
    /// slug character, so nothing that exists is refused.
    /// </summary>
    public static bool IsTraversalSegment(string segment)
    {
        if (IsDotSegment(segment)) return true;
        if (segment.IndexOfAny(Redirecting) >= 0) return true;
        var decoded = Uri.UnescapeDataString(segment);
        return decoded.Contains('/') || decoded.IndexOfAny(Redirecting) >= 0;
    }

    /// <summary>
    /// True if the segment is <c>.</c> or <c>..</c>, as given or after one percent-decoding. The one
    /// definition of a dot segment, used wherever a name becomes part of a path: URI paths here,
    /// METS paths in the parser, and slugs.
    /// </summary>
    public static bool IsDotSegment(string segment) =>
        segment is "." or ".." || Uri.UnescapeDataString(segment) is "." or "..";

    /// <summary>
    /// True if the segment, after one percent-decoding, contains a path separator of either kind:
    /// <c>%2f</c> or <c>%5c</c> spelled into a single segment. Nothing here decodes a path twice,
    /// so such a segment cannot traverse - but a boundary check should refuse an alternate spelling
    /// of a separator rather than reason about what would happen to it.
    /// <see cref="Uri.UnescapeDataString"/> leaves a malformed sequence as it is and does not throw.
    /// </summary>
    public static bool ContainsEncodedSeparator(string segment)
    {
        var decoded = Uri.UnescapeDataString(segment);
        return decoded.Contains('/') || decoded.Contains('\\');
    }

    private static readonly char[] Redirecting = ['\\', '\0', '#', '?', ':'];
}
