using System.Net;
using DigitalPreservation.Core.Web.Headers;
using Test.Helpers;

namespace Preservation.API.Tests.Integration;

/// <summary>
/// Issue #386: HEAD /repository or /repository/ (the root, no path at all) left [ApiController]'s
/// automatic model validation to answer 400 with FluentValidation's generic "field is required"
/// shape before HeadResource's body ever ran - an accidental artifact of path being declared
/// non-nullable with no default, not a deliberate decision.
///
/// Review feedback on the first version of this fix (PR #391) pointed out that the root IS a real,
/// documented resource - GET /repository works in every deployed environment and returns a
/// Container typed "RepositoryRoot" (Fedora reports it as fedora:RepositoryRoot). So HEAD's answer
/// must agree with GET's: 200 with the resource-type header, not a 400 refusal. See
/// RepositoryRootBrowseTests.cs for GET's own coverage of that - it needs a faked IMediator rather
/// than this class's real HTTP round trip, because GET's root case (unlike HEAD's) still calls
/// through to Storage API, which DigitalPreservationAppFactory has no backing instance of.
///
/// This stays an HTTP-level test because the bug lives in routing and model binding - a controller
/// test calling HeadResource(path) directly, as Features/Repository/HeadResourceTests.cs does,
/// bypasses both and can't catch it. HEAD's root case is still safe to exercise this way because it
/// never reaches the mediator: RepositoryController answers it directly, so there is no backing
/// Storage API call for the test host to be missing.
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
    [InlineData("/Repository")]
    public async Task Head_RepositoryRoot_Returns200_AgreeingWithGet(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, path);

        var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(HttpHeaders.XPreservationResourceType).Should().ContainSingle("RepositoryRoot");
    }
}
