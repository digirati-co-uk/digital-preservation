namespace DigitalPreservation.Common.Model.Transit;

/// <summary>
/// Parses a BagIt checksum manifest (manifest-sha256.txt and friends - see
/// <see cref="FolderNames.BagitFiles"/>) into a map of path to digest. Shared between
/// DigitalPreservation.Workspace's reading of a deposit's own manifest and Pipeline.API's
/// before/after digest comparison (issue #388 item 3.2), which previously copied this parsing word
/// for word, including the same bug: splitting every line on every space and keeping only lines
/// that came out to exactly two parts silently drops any path BagIt allows to contain a space.
/// </summary>
public static class BagItManifest
{
    public static async Task<Dictionary<string, string>> ParseAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        return Parse(text);
    }

    public static Dictionary<string, string> Parse(string manifestText)
    {
        var pathsByDigest = new Dictionary<string, string>();
        foreach (var rawLine in manifestText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Split on the first whitespace only - both previous copies split on every space and
            // discarded any line that didn't come out to exactly two parts.
            var separatorIndex = line.IndexOf(' ');
            if (separatorIndex < 0)
            {
                continue;
            }

            var digest = line[..separatorIndex];
            var path = line[(separatorIndex + 1)..].TrimStart();
            if (path.Length == 0)
            {
                continue;
            }

            pathsByDigest.Add(path, digest);
        }

        return pathsByDigest;
    }
}
