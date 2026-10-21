using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository;
using Preservation.API.Features.Repository.Requests;

namespace Preservation.API.Tests.Features.Repository;

/// <summary>
/// Review feedback on the first version of issue #386's fix (PR #391) pointed out that GET
/// /repository and /repository/ (the root) already work in every deployed environment: the UI's
/// Browse page depends on it, the docs site documents it, and it returns a Container typed
/// "RepositoryRoot" (Fedora reports it as fedora:RepositoryRoot). The 400 guard the first version
/// added to Browse for this case would have broken that - the unhandled exception it was meant to
/// fix was most likely a test-host artifact (DigitalPreservationAppFactory has no Storage API
/// behind it), not something real traffic hits.
///
/// So this pins the opposite behaviour: the root passes straight through to the mediator, exactly
/// like any other path, rather than being refused. A fake IMediator is used rather than a real HTTP
/// round trip (c.f. Integration/RepositoryRootTests.cs) because the real round trip is exactly what
/// the test host can't do for this path - there's no backing Storage API to answer it.
/// </summary>
public class RepositoryRootBrowseTests
{
    private readonly IMediator mediator = A.Fake<IMediator>();

    private RepositoryController ControllerAt(string requestPath) =>
        new(mediator, A.Fake<IMetsParser>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { Path = requestPath } }
            }
        };

    [Theory]
    [InlineData("/repository")]
    [InlineData("/repository/")]
    public async Task Get_RepositoryRoot_PassesThroughToTheMediator_RatherThanBeingRefused(string requestPath)
    {
        var repositoryRoot = new Container { Type = "RepositoryRoot" };
        A.CallTo(() => mediator.Send(A<GetResource>._, A<CancellationToken>._))
            .Returns(Result.Ok<PreservedResource?>(repositoryRoot));

        var response = await ControllerAt(requestPath).Browse();

        A.CallTo(() => mediator.Send(
                A<GetResource>.That.Matches(r => r.Path == requestPath), A<CancellationToken>._))
            .MustHaveHappened();
        var ok = response.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(repositoryRoot);
    }
}
