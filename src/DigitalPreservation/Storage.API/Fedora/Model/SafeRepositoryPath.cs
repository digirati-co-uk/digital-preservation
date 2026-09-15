namespace Storage.API.Fedora.Model;

/// <summary>
/// Whether a caller-supplied "path under the repository root" can only ever name something under
/// that root once it is resolved against the Fedora base URI.
/// </summary>
/// <remarks>
/// <see cref="Converters.GetFedoraUri"/> resolves the path as a relative reference against
/// <c>FedoraOptions.Root</c>, and <see cref="System.Uri"/> relative-reference resolution will happily
/// leave the root: <c>//evil.com/x</c> becomes a different host, <c>/x</c> the Fedora host's own root,
/// <c>../x</c> the parent, and <c>%2e%2e/x</c> is canonicalised to the same as <c>../x</c>. The Fedora
/// client sends the admin credential on every request, so any of those is a request to the wrong
/// place carrying the wrong secret. This is the single rule for what a path may look like: non-empty
/// segments separated by single slashes, none of which is a dot segment before or after percent-decoding,
/// nothing that could be read as a scheme, and no backslashes or NULs. A single trailing slash is
/// tolerated because existing callers produce one. Segments are otherwise left alone: <c>%</c> is a legal
/// slug character, so <c>a%2Fb</c> is one segment and must stay one.
/// </remarks>
public static class SafeRepositoryPath
{
    public static bool IsUnderRoot(string? pathUnderRoot, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(pathUnderRoot))
        {
            return true; // the root itself
        }

        if (pathUnderRoot.Contains('\\') || pathUnderRoot.Contains('\0'))
        {
            reason = "contains a backslash or NUL";
            return false;
        }

        var segments = pathUnderRoot.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                if (i == segments.Length - 1 && i > 0)
                {
                    continue; // a single trailing slash
                }
                reason = "has an empty segment (a leading, doubled or lone '/')";
                return false;
            }

            if (i == 0 && segment.Contains(':'))
            {
                reason = "first segment contains ':' and could be read as a scheme";
                return false;
            }

            var decoded = Uri.UnescapeDataString(segment);
            if (segment is "." or ".." || decoded is "." or "..")
            {
                reason = "contains a dot segment";
                return false;
            }
        }

        return true;
    }
}
