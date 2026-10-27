using Amazon.S3.Model;
using Storage.Repository.Common;

namespace Storage.API.Tests.S3;

/// <summary>
/// SetOriginalName/GetOriginalName replace two different ways the original-name S3 metadata was
/// written (issue #388 item 3.3): URL-encoded in CreateFolder/UploadFileToDeposit, but raw in two
/// spots in Storage.cs (PutDirectory and WriteDepositFileSystem), while every reader always
/// UrlDecodes. These pin the fix: every writer now goes through SetOriginalName, and a name that a
/// raw write followed by a UrlDecode read would have corrupted now round-trips correctly.
/// </summary>
public class S3HelpersOriginalNameTests
{
    [Theory]
    [InlineData("ordinary-name.txt")]
    [InlineData("a name with spaces.txt")]
    [InlineData("a+b.txt")]
    [InlineData("Teto in tree.png")]
    [InlineData("été.tif")]
    public void SetOriginalName_And_GetOriginalName_Round_Trip(string name)
    {
        var metadata = new PutObjectRequest().Metadata;

        metadata.SetOriginalName(name);

        metadata.GetOriginalName().Should().Be(name);
    }

    [Fact]
    public void A_Name_Containing_A_Literal_Percent_Escape_Round_Trips_Correctly()
    {
        // The Diverged bug this closes: a raw write (as PutDirectory and WriteDepositFileSystem
        // used to do) followed by GetOriginalName's UrlDecode would turn the literal "%20" into a
        // space, corrupting the name to "fileA nameB.txt". SetOriginalName's own UrlEncode escapes
        // the '%' first, so the round trip is exact.
        const string name = "fileA%20nameB.txt";
        var metadata = new PutObjectRequest().Metadata;

        metadata.SetOriginalName(name);

        metadata.GetOriginalName().Should().Be(name);
    }

    [Fact]
    public void GetOriginalName_Returns_Null_When_No_Original_Name_Was_Set()
    {
        var metadata = new PutObjectRequest().Metadata;

        metadata.GetOriginalName().Should().BeNull();
    }
}
