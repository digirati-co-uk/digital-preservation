using System.Net;
using Test.Helpers;

namespace Storage.API.Tests.Integration;

/// <summary>
/// The route-value filter is registered globally; these prove it is actually in front of each
/// path-taking controller, by sending paths that would resolve outside the repository root and
/// expecting a 400 before any handler (and therefore Fedora) is reached. Kestrel itself rejects a
/// raw ".." segment, so the doubly-encoded form is what an attacker would send.
/// </summary>
[Trait("Category", "Integration")]
public class RepositoryPathFilterTests : IClassFixture<DigitalPreservationAppFactory<Program>>
{
    private readonly HttpClient httpClient;

    public RepositoryPathFilterTests(DigitalPreservationAppFactory<Program> factory)
    {
        httpClient = factory.CreateClient();
    }

    [Theory]
    [InlineData("/repository/%252e%252e/x")]
    [InlineData("/content/%252e%252e/x")]
    [InlineData("/import/test-path/%2F%2Fevil.com%2Fx")]
    [InlineData("/import/test-path/%252e%252e/x")]
    [InlineData("/ocfl/storagemap/%252e%252e/x")]
    [InlineData("/repository/cc/thing/fcr:tombstone")]
    public async Task A_Path_That_Would_Leave_The_Root_Is_Refused_Before_The_Handler(string url)
    {
        var response = await httpClient.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("repository root");
    }
}
