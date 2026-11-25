using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Storage.Ocfl;

namespace Storage.API.Ocfl;

/// <summary>
/// The files an OCFL object used to have (issue #261): every logical path in a version before the
/// given one that the given version does not have, with the last version it was in and where its
/// content was then. Worked out from the inventory alone, which already holds every version's state.
/// </summary>
public static class FormerFiles
{
    public static Dictionary<string, FormerOriginFile> Of(Inventory inventory, string version, Func<string, bool> isIgnored)
    {
        var ordered = inventory.Versions.Keys.OrderBy(VersionNumber).ToList();
        var index = ordered.IndexOf(version);
        if (index < 0)
        {
            throw new ArgumentException($"Version {version} is not in the inventory", nameof(version));
        }

        var current = PathsIn(inventory.Versions[version], isIgnored).Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        var former = new Dictionary<string, FormerOriginFile>(StringComparer.Ordinal);
        // Oldest first, so a path's last appearance wins.
        foreach (var earlier in ordered.Take(index))
        {
            foreach (var (path, digest) in PathsIn(inventory.Versions[earlier], isIgnored))
            {
                if (current.Contains(path) || !inventory.Manifest.TryGetValue(digest, out var contentPaths))
                {
                    continue;
                }
                former[path] = new FormerOriginFile
                {
                    Hash = digest,
                    FullPath = contentPaths[0],
                    LastVersion = earlier
                };
            }
        }
        return former;
    }

    private static IEnumerable<(string Path, string Digest)> PathsIn(DigitalPreservation.Common.Model.Storage.Ocfl.Version version,
        Func<string, bool> isIgnored) =>
        version.State.SelectMany(kvp => kvp.Value.Where(p => !isIgnored(p)).Select(p => (p, kvp.Key)));

    // OCFL version names are "v" and a number, possibly zero-padded ("v1", "v0003"); compare the
    // numbers, not the strings, so v10 comes after v9.
    private static int VersionNumber(string name) =>
        int.TryParse(name.AsSpan(1), out var number) ? number : int.MaxValue;
}
