using System.Net;
using Test.Helpers;

namespace Preservation.API.Tests.Integration;

/// <summary>
/// Issue #386: HEAD /repository or /repository/ (the root, no path at all) left [ApiController]'s
/// automatic model validation to answer 400 with FluentValidation's generic "field is required"
/// shape before HeadResource's body ever ran - an accidental artifact of path being declared
/// non-nullable with no default, not a deliberate decision. GET /repository(/) was worse: path
/// binds to null the same way, but Browse never checked it, and something downstream (traced to
/// CommonApiBase.GetResourceInternal's new Uri(path, UriKind.Relative) call) threw an unhandled
/// ArgumentNullException, surfacing to the caller as a bare 500.
///
/// A controller test calling HeadResource(path) or Browse(path) directly, as the existing
/// Features/Repository/HeadResourceTests.cs does, can't catch either case: both bugs live in
/// routing and model binding, which a direct method call bypasses entirely. These are HTTP-level
/// tests for that reason, following Integration/BasicTest.cs's pattern.
/// </summary>
[Trait("Category", "Integration")]
public class RepositoryRootTests : IClassFixture<DigitalPreservationAppFactory<Program>>
{
    private readonly HttpClient httpClient;

    public RepositoryRootTests(DigitalPreservationAppFactory<Program> factory)
    {
        httpClient = factory.CreateClient();
    }

    [Theory]
    [InlineData("/repository")]
    [InlineData("/repository/")]
    public async Task Head_RepositoryRoot_Returns400_AsADeliberateDecision(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, path);

        var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/repository")]
    [InlineData("/repository/")]
    public async Task Get_RepositoryRoot_Returns400_NotAnUnhandledException(string path)
    {
        var response = await httpClient.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("No resource path given");
    }
}
