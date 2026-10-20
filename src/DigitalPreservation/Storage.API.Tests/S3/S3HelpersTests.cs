using Storage.Repository.Common;

namespace Storage.API.Tests.S3;

/// <summary>
/// BuildDepositObjectKey replaces three different ways a deposit's S3 object key got built
/// (issue #388 item 3.1): StringUtils.BuildPath in CreateFolder/UploadFileToDeposit, raw string
/// concatenation in DeleteItems, and a third spelling also in DeleteItems. Diverged: plain
/// concatenation didn't trim whitespace from a segment the way BuildPath does, so an upload and a
/// delete of what a caller would consider the same path could end up with different S3 keys.
/// </summary>
public class S3HelpersTests
{
    [Fact]
    public void Joins_The_Root_Key_With_Every_Segment()
    {
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", "data/", "objects/sub", "page-001.jpg")
            .Should().Be("deposits/dep-1/deposit-files/data/objects/sub/page-001.jpg");
    }

    [Fact]
    public void Drops_Empty_Or_Null_Segments()
    {
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", string.Empty, null, "page-001.jpg")
            .Should().Be("deposits/dep-1/deposit-files/page-001.jpg");
    }

    [Fact]
    public void Trims_Whitespace_From_Every_Segment()
    {
        // The Diverged bug this closes: raw string concatenation (as DeleteItems used to do) would
        // keep a leading/trailing space verbatim, giving a different key than an upload of what a
        // caller would consider the same path.
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", " page-001.jpg ")
            .Should().Be("deposits/dep-1/deposit-files/page-001.jpg");
    }

    [Fact]
    public void Collapses_Stray_Leading_And_Trailing_Slashes_Between_Segments()
    {
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", "/objects/", "/page-001.jpg")
            .Should().Be("deposits/dep-1/deposit-files/objects/page-001.jpg");
    }
}
