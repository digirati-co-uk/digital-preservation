namespace DigitalPreservation.Core.Tests;

/// <summary>
/// The single shared rule behind an export destination check, used by both APIs so the parsing and
/// the wording can't drift (issue #288). Tested in isolation from either API's DI/config.
/// </summary>
public class ExportDestinationTests
{
    private static readonly string[] PermittedBuckets = ["working-bucket", "goobi-deposits"];

    [Theory]
    [InlineData("s3://working-bucket/export-of-my-ag")]
    [InlineData("https://working-bucket.s3.amazonaws.com/export-of-my-ag")]
    [InlineData("s3://goobi-deposits/export-of-my-ag")]
    public void A_Destination_In_A_Permitted_Bucket_Is_Accepted(string destination)
    {
        ExportDestination.IsInBucket(new Uri(destination), PermittedBuckets, out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void A_Destination_In_A_Different_Bucket_Is_Refused_And_The_Reason_Names_It()
    {
        var result = ExportDestination.IsInBucket(
            new Uri("s3://someone-elses-bucket/export-of-my-ag"), PermittedBuckets, out var reason);

        result.Should().BeFalse();
        reason.Should().Contain("someone-elses-bucket");
    }

    [Fact]
    public void An_Unparseable_Destination_Is_Refused()
    {
        var result = ExportDestination.IsInBucket(
            new Uri("https://example.com/not-an-s3-uri"), PermittedBuckets, out var reason);

        result.Should().BeFalse();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void An_Empty_Permitted_List_Refuses_Everything()
    {
        var result = ExportDestination.IsInBucket(
            new Uri("s3://working-bucket/export-of-my-ag"), [], out var reason);

        result.Should().BeFalse();
        reason.Should().NotBeNull();
    }
}
