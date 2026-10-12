using Preservation.API.Features.MediaServer;

namespace Preservation.API.Tests.Features.MediaServer;

/// <summary>
/// The anonymous media route's own path check. FindFile is the real gate, but this is what stops a
/// path from ever being turned into an S3 URI outside the deposit - and routing decodes the route
/// value only once, so "%2e%2e" must be treated as the dot segment System.Uri will make of it.
/// </summary>
public class MediaPathValidationTests
{
    [Theory]
    [InlineData("objects/page-001.tif")]
    [InlineData("objects/sub/page-001.tif")]
    [InlineData("objects/page-001.tif/info.json")]
    [InlineData("objects/page-001.tif/full/max/0/default.jpg")]
    [InlineData("objects/.hidden.jpg")]
    [InlineData("objects/a%20b.jpg")]
    public void Ordinary_Paths_Are_Accepted(string path)
    {
        MediaController.ValidateLocalPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../objects/x.jpg")]
    [InlineData("objects/../../other/objects/x.jpg")]
    [InlineData("objects/./x.jpg")]
    [InlineData("%2e%2e/other/objects/x.jpg")]
    [InlineData("objects/%2E%2E/%2E%2E/other/objects/x.jpg")]
    [InlineData("objects/%2e/x.jpg")]
    [InlineData("objects//x.jpg")]
    [InlineData("/objects/x.jpg")]
    [InlineData("objects/a%2Fb.jpg")]
    [InlineData("objects/a%5Cb.jpg")]
    [InlineData("objects\\x.jpg")]
    [InlineData("objects/x.jpg%23y")]
    [InlineData("objects/x.jpg%3Fy")]
    public void Paths_That_Could_Leave_The_Deposit_Are_Refused(string path)
    {
        MediaController.ValidateLocalPath(path).Should().BeFalse();
    }
}
