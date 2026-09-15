namespace DigitalPreservation.Utils;

public static class PathX
{
    /// <summary>
    /// Whether <paramref name="candidate"/> resolves, via <see cref="Path.GetFullPath(string)"/>, to
    /// <paramref name="root"/> itself or a path below it.
    /// </summary>
    /// <remarks>
    /// Callers that build a path by combining a root directory with a caller-controlled segment must
    /// check this before touching the filesystem: <see cref="Path.Combine(string,string)"/> silently
    /// discards the root when the second argument is itself rooted (e.g. "/etc"), and
    /// <see cref="Path.GetFullPath(string)"/> resolves ".." segments lexically without regard to
    /// where they end up - neither stops a caller-controlled value from escaping <paramref name="root"/>.
    /// </remarks>
    public static bool IsUnderRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);

        return fullCandidate.Equals(fullRoot, comparison)
               || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }
}
