using System.Security.Cryptography;
using System.Text;
using Storage.API.Fedora;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// RelativeToRoot used Convert.ToHexString(hash).ToLower() to build its id digest, one of three
/// ways the codebase produced lowercase hex (issue #388 item 3.4), now Convert.ToHexStringLower.
/// No test previously pinned this method's output at all, which matters here because it decides
/// the OCFL S3 path an Archival Group is actually stored under - a hex-casing regression here
/// would be a real, hard-to-reverse storage-layout change, not just a cosmetic one.
/// </summary>
public class RepositoryPathRelativeToRootTests
{
    [Fact]
    public void RelativeToRoot_Tuples_The_Lowercase_Hex_Digest_Of_The_Fedora_Id()
    {
        const string prefix = "fcrepo/";
        const string id = "cc/thing";

        var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"info:fedora/{id}")));
        var expected = $"{prefix}{expectedDigest[..3]}/{expectedDigest[3..6]}/{expectedDigest[6..9]}/{expectedDigest}";

        RepositoryPath.RelativeToRoot(prefix, id).Should().Be(expected);
    }

    [Fact]
    public void RelativeToRoot_Produces_Only_Lowercase_Hex_Characters()
    {
        var result = RepositoryPath.RelativeToRoot("fcrepo/", "cc/another-thing");

        result.Should().MatchRegex("^fcrepo/[0-9a-f]{3}/[0-9a-f]{3}/[0-9a-f]{3}/[0-9a-f]{64}$");
    }
}
