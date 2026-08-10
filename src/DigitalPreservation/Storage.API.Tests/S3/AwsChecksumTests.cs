using Storage.Repository.Common;

namespace Storage.API.Tests.S3;

public class AwsChecksumTests
{
    [Fact]
    public void FromBase64ToHex_Converts_Base64_Sha256_To_Lowercase_Hex()
    {
        // "hello world" SHA256, base64-encoded as AWS returns it
        const string base64 = "uU0nuZNNPgilLlLX2n2r+sSE7+N6U4DukIj3rOLvzek=";

        var hex = AwsChecksum.FromBase64ToHex(base64);

        hex.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromBase64ToHex_Returns_Null_For_Missing_Input(string? base64)
    {
        var hex = AwsChecksum.FromBase64ToHex(base64);

        hex.Should().BeNull();
    }
}
