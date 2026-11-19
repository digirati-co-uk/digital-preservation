using System.Security.Cryptography;
using System.Text;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Core.Tests.Strings;

/// <summary>
/// Checksum.FromByteArray used to build lowercase hex with a StringBuilder and "x2" formatting, one
/// of three ways the codebase produced lowercase hex (issue #388 item 3.4 - the other two are
/// AwsChecksum.FromBase64ToHex and the ToHexString().ToLower() call sites in RepositoryPath and
/// TokenService). Now all four go through .NET 10's Convert.ToHexStringLower. Sha256FromFile and
/// Sha256FromStream (and their SHA512 equivalents) also lost their create-and-dispose
/// SHA256/SHA512 instance in favour of the static SHA256.HashData/SHA512.HashData passed straight
/// into HashFromStream, which these also pin against an independent computation.
/// </summary>
public class ChecksumTests
{
    [Fact]
    public void FromByteArray_Produces_Lowercase_Hex()
    {
        Checksum.FromByteArray([0x00, 0x1a, 0xff]).Should().Be("001aff");
    }

    [Fact]
    public void Sha256FromStream_Matches_The_WellKnown_Empty_Input_Digest()
    {
        using var stream = new MemoryStream();
        Checksum.Sha256FromStream(stream).Should().Be(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Sha256FromStream_Agrees_With_An_Independently_Computed_Digest()
    {
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox");
        using var stream = new MemoryStream(bytes);

        Checksum.Sha256FromStream(stream).Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Sha512FromStream_Agrees_With_An_Independently_Computed_Digest()
    {
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox");
        using var stream = new MemoryStream(bytes);

        Checksum.Sha512FromStream(stream).Should().Be(Convert.ToHexStringLower(SHA512.HashData(bytes)));
    }

    [Fact]
    public void Sha256FromFile_Hashes_The_Files_Contents()
    {
        var path = Path.GetTempFileName();
        try
        {
            var contents = Encoding.UTF8.GetBytes("file contents");
            File.WriteAllBytes(path, contents);

            Checksum.Sha256FromFile(new FileInfo(path)).Should().Be(Convert.ToHexStringLower(SHA256.HashData(contents)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256FromFile_Reads_From_The_Start_Even_If_The_Stream_Was_Already_Advanced()
    {
        // HashFromStream resets Position to 0 on a seekable stream before hashing - pinned here via
        // Sha256FromStream, since FileStream (used by Sha256FromFile) is always seekable anyway.
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox");
        using var stream = new MemoryStream(bytes);
        stream.Position = bytes.Length;

        Checksum.Sha256FromStream(stream).Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
