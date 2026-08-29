using DigitalPreservation.Core.Auth;

namespace DigitalPreservation.Core.Tests.Auth;

public class ClientDirectoryTests
{
    private static ClientDirectory Build() => new(new Dictionary<string, ClientProfile>
    {
        ["22222222-2222-2222-2222-222222222222"] = new() { Name = "goobi", DepositBucket = "leeds-goobi-deposits" },
        ["11111111-1111-1111-1111-111111111111"] = new() { Name = "iiif-builder" }
    });

    [Fact]
    public void TryResolve_KnownAppId_ReturnsProfile()
    {
        var directory = Build();

        directory.TryResolve("22222222-2222-2222-2222-222222222222", out var profile).Should().BeTrue();

        profile!.Name.Should().Be("goobi");
        profile.DepositBucket.Should().Be("leeds-goobi-deposits");
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        var directory = Build();

        directory.TryResolve("11111111-1111-1111-1111-111111111111".ToUpperInvariant(), out var profile)
            .Should().BeTrue();

        profile!.Name.Should().Be("iiif-builder");
        profile.DepositBucket.Should().BeNull();
    }

    [Fact]
    public void TryResolve_UnknownAppId_ReturnsFalse()
    {
        Build().TryResolve("99999999-9999-9999-9999-999999999999", out var profile).Should().BeFalse();
        profile.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolve_NullOrEmptyAppId_ReturnsFalse(string? appId)
    {
        Build().TryResolve(appId, out var profile).Should().BeFalse();
        profile.Should().BeNull();
    }

    [Fact]
    public void EmptyDirectory_ResolvesNothing()
    {
        var empty = new ClientDirectory(new Dictionary<string, ClientProfile>());

        empty.TryResolve("11111111-1111-1111-1111-111111111111", out var profile).Should().BeFalse();
        profile.Should().BeNull();
    }
}
