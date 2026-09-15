using DigitalPreservation.Utils;

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
/// segments separated by single slashes, none of which is a traversal segment
/// (<see cref="UriPathX.IsTraversalSegment"/>: a dot segment, separator or NUL, before or after one
/// more percent-decoding), and nothing that could be read as a scheme. A single trailing slash is
/// tolerated because existing callers produce one.
///
/// Applied twice: by <see cref="Web.RepositoryPathFilter"/> to route values, for a 400; and by the
/// import and export queue handlers to the paths inside a posted job, for the same. Body-bound paths
/// never pass through the filter, so both are needed. Distinct from
/// <see cref="PathX.IsUnderRoot"/>, which is for resolved local filesystem paths.
/// </remarks>
public static class SafeRepositoryPath
{
    public static bool IsRepositoryPath(string? pathUnderRoot, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(pathUnderRoot))
        {
            return true; // the root itself
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

            if (UriPathX.IsTraversalSegment(segment))
            {
                reason = "contains a dot segment, a separator or a NUL, before or after decoding";
                return false;
            }
        }

        return true;
    }
}
