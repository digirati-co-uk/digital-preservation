using Storage.Repository.Common;

namespace Storage.API.Tests.S3;

/// <summary>
/// BuildDepositObjectKey replaces three different ways a deposit's S3 object key got built
/// (issue #388 item 3.1): StringUtils.BuildPath in CreateFolder/UploadFileToDeposit, raw string
/// concatenation in DeleteItems, and a third spelling also in DeleteItems.
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
    public void Preserves_Whitespace_Inside_A_Segment()
    {
        // A file written straight to S3, bypassing the platform's own slug validation, can
        // genuinely have a leading or trailing space in its name. Trimming the key built for a
        // delete of such a file would address a different key than the one it was uploaded
        // under - this is the bug a real-world review caught: the delete either throws or, since
        // S3's DeleteObject is idempotent, silently reports success for a key that was never there.
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", " page-001.jpg ")
            .Should().Be("deposits/dep-1/deposit-files/ page-001.jpg ");
    }

    [Fact]
    public void Collapses_Stray_Leading_And_Trailing_Slashes_Between_Segments()
    {
        S3Helpers.BuildDepositObjectKey("deposits/dep-1/deposit-files/", "/objects/", "/page-001.jpg")
            .Should().Be("deposits/dep-1/deposit-files/objects/page-001.jpg");
    }
}
