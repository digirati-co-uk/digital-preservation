using DigitalPreservation.UI.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace DigitalPreservation.UI.Tests;

/// <summary>
/// What the Status page says the image was built from (issue #489). The values arrive as
/// Build__* environment variables, set by Dockerfile.UI.
/// </summary>
public class BuildInfoTests
{
    [Fact]
    public void A_Local_Build_Is_Not_Known_And_Has_No_Links()
    {
        var build = new BuildInfo();

        build.IsKnown.Should().BeFalse();
        build.CommitUrl.Should().BeNull();
        build.PullRequestUrl.Should().BeNull();
        build.CommitDateUtc.Should().BeNull();
    }

    [Fact]
    public void The_Environment_Variables_Bind_To_The_Build_Section_And_Make_Links()
    {
        var build = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // As ASP.NET Core reads Build__Commit and its siblings from the environment
                ["Build:Commit"] = "95c9c658aa11bb22cc33dd44ee55ff6677889900",
                ["Build:Ref"] = "main",
                ["Build:PullRequest"] = "485",
                ["Build:Repository"] = "https://github.com/digirati-co-uk/digital-preservation"
            })
            .Build().GetSection(BuildInfo.Build).Get<BuildInfo>()!;

        build.IsKnown.Should().BeTrue();
        build.CommitUrl.Should().Be("https://github.com/digirati-co-uk/digital-preservation/commit/95c9c658aa11bb22cc33dd44ee55ff6677889900");
        build.RefUrl.Should().Be("https://github.com/digirati-co-uk/digital-preservation/tree/main");
        build.PullRequestUrl.Should().Be("https://github.com/digirati-co-uk/digital-preservation/pull/485");
    }

    [Fact]
    public void A_Build_With_No_Pull_Request_Has_No_Pull_Request_Link()
    {
        var build = new BuildInfo { Commit = "95c9c658", Repository = "https://github.com/digirati-co-uk/digital-preservation" };

        build.PullRequestUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("2026-11-20T15:24:28Z", "2026-11-20 15:24 UTC")]
    [InlineData("2026-07-01T16:24:28+01:00", "2026-07-01 15:24 UTC")]
    [InlineData("not a date", "not a date")]
    public void Dates_Are_Shown_In_UTC_Or_As_They_Came(string date, string shown)
    {
        new BuildInfo { CommitDate = date, Date = date }.CommitDateUtc.Should().Be(shown);
        new BuildInfo { CommitDate = date, Date = date }.DateUtc.Should().Be(shown);
    }
}
