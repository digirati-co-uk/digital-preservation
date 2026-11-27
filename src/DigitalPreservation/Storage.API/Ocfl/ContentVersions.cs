using DigitalPreservation.Common.Model.Storage.Ocfl;

namespace Storage.API.Ocfl;

/// <summary>
/// For each file in a version of an OCFL object, the version in which that path last got the
/// content it has now (issue #261): the earliest version of the unbroken run, ending at this one,
/// in which the path has had this digest. Not the version in the content path, which names where
/// the bytes were first stored: OCFL stores identical bytes once, so a file reverted to earlier
/// content points back at the old copy although it changed in this version.
/// </summary>
public static class ContentVersions
{
    public static Dictionary<string, string> Of(Inventory inventory, string version)
    {
        var ordered = inventory.Versions.Keys.OrderBy(FormerFiles.VersionNumber).ToList();
        var index = ordered.IndexOf(version);
        if (index < 0)
        {
            throw new ArgumentException($"Version {version} is not in the inventory", nameof(version));
        }

        var digests = ordered.Take(index + 1).Select(v => DigestsByPath(inventory.Versions[v])).ToList();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, digest) in digests[index])
        {
            var since = index;
            while (since > 0 && digests[since - 1].TryGetValue(path, out var earlier) && earlier == digest)
            {
                since--;
            }
            result[path] = ordered[since];
        }
        return result;
    }

    private static Dictionary<string, string> DigestsByPath(DigitalPreservation.Common.Model.Storage.Ocfl.Version version)
    {
        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (digest, paths) in version.State)
        {
            foreach (var path in paths)
            {
                byPath[path] = digest;
            }
        }
        return byPath;
    }
}
