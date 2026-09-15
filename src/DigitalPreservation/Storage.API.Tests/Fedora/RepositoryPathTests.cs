using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// The Fedora client sends the admin credential on every request, and the Fedora URI is built by
/// resolving a caller-supplied path against the root. These pin the rule that keeps that resolution
/// under the root, both as the validator callers use and as GetFedoraUri's own last line of defence.
/// </summary>
public class RepositoryPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cc")]
    [InlineData("cc/thing")]
    [InlineData("cc/thing/")]
    [InlineData("cc/thing/objects/page-001.tif")]
    [InlineData("cc/a%2Fb")]                 // '%' is a legal slug character; one segment, stays one
    [InlineData("cc/a%252e%252e")]           // decodes once to a%2e%2e, which Uri leaves alone
    [InlineData("cc/.hidden")]
    [InlineData("cc/...")]
    public void Paths_Under_The_Root_Are_Accepted(string? path)
    {
        SafeRepositoryPath.IsUnderRoot(path, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("//evil.com/x", "empty segment")]       // resolves to another host
    [InlineData("/x", "empty segment")]                 // resolves to the Fedora host's root
    [InlineData("cc//thing", "empty segment")]
    [InlineData("/", "empty segment")]
    [InlineData("../x", "dot segment")]
    [InlineData("cc/../../x", "dot segment")]
    [InlineData("cc/./x", "dot segment")]
    [InlineData("%2e%2e/x", "dot segment")]             // Uri canonicalises %2e as '.'
    [InlineData("cc/%2E%2E/x", "dot segment")]
    [InlineData("cc/%2e", "dot segment")]
    [InlineData("http://evil.com/x", "scheme")]
    [InlineData("http:evil", "scheme")]
    [InlineData("cc\\thing", "backslash")]
    public void Paths_That_Would_Leave_The_Root_Are_Refused(string path, string expectedReason)
    {
        SafeRepositoryPath.IsUnderRoot(path, out var reason).Should().BeFalse();
        reason.Should().Contain(expectedReason);
    }

    private static readonly Uri Root = new("http://fedora:8080/fcrepo/rest/");

    private static Converters MakeConverters() => new(
        Options.Create(new FedoraOptions { Root = Root, AdminUser = "fedoraAdmin", AdminPassword = "not-a-real-password", Bucket = "ocfl", OcflS3Prefix = "fcrepo/" }),
        Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") }));

    [Theory]
    [InlineData("cc/thing", "http://fedora:8080/fcrepo/rest/cc/thing")]
    [InlineData("cc/a%2Fb", "http://fedora:8080/fcrepo/rest/cc/a%2Fb")]
    [InlineData("", "http://fedora:8080/fcrepo/rest/")]
    public void GetFedoraUri_Resolves_Paths_Under_The_Root(string path, string expected)
    {
        MakeConverters().GetFedoraUri(path).Should().Be(new Uri(expected));
    }

    [Theory]
    [InlineData("//evil.com/x")]
    [InlineData("/x")]
    [InlineData("../x")]
    [InlineData("cc/../../../x")]
    [InlineData("%2e%2e/x")]
    [InlineData("http://evil.com/x")]
    public void GetFedoraUri_Refuses_Paths_That_Would_Leave_The_Root(string path)
    {
        var act = () => MakeConverters().GetFedoraUri(path);
        act.Should().Throw<ArgumentException>().WithMessage("*repository root*");
    }
}
