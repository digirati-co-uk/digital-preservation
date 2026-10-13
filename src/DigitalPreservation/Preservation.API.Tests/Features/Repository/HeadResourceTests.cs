using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Mets;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository;
using Preservation.API.Features.Repository.Requests;

namespace Preservation.API.Tests.Features.Repository;

/// <summary>
/// HeadResource used to send GetResourceType the route-bound path parameter - the {*path}
/// catch-all's value, which is everything *after* "repository/" (e.g. "cc") - while
/// GetResourceTypeHandler passes it straight through to IStorageApiClient.GetResourceType, which
/// is documented to need the *full* /repository/... path and HEADs it as-is. So every HEAD
/// request 404'd, including for paths that exist (issue #348). Fixed by sending Request.Path (the
/// full path) instead, the same source Browse already uses for this via
/// RepositoryController.TryStreamConventionalMets and GetResource - not by reshaping the path
/// inside GetResourceTypeHandler, which already serves that caller correctly and would have its
/// prefix doubled by any change there.
/// </summary>
public class HeadResourceTests
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
    [InlineData("/repository/cc", "cc")]
    [InlineData("/repository/a/b/c", "a/b/c")]
    public async Task Sends_The_Full_Repository_Path_Not_Just_The_Route_Segment(string requestPath, string routePath)
    {
        A.CallTo(() => mediator.Send(A<GetResourceType>._, A<CancellationToken>._))
            .Returns(Result.Ok<string?>(nameof(Container)));

        await ControllerAt(requestPath).HeadResource(routePath);

        A.CallTo(() => mediator.Send(
                A<GetResourceType>.That.Matches(r => r.Path == requestPath), A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Fact]
    public async Task A_Successful_Lookup_Gives_200_With_The_Resource_Type_Header()
    {
        var controller = ControllerAt("/repository/cc");
        A.CallTo(() => mediator.Send(A<GetResourceType>._, A<CancellationToken>._))
            .Returns(Result.Ok<string?>(nameof(Container)));

        await controller.HeadResource("cc");

        controller.Response.StatusCode.Should().Be(200);
        controller.Response.Headers[HttpHeaders.XPreservationResourceType].ToString().Should().Be(nameof(Container));
    }

    [Fact]
    public async Task A_NotFound_Failure_Gives_404()
    {
        var controller = ControllerAt("/repository/no-such-thing");
        A.CallTo(() => mediator.Send(A<GetResourceType>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<string?>(ErrorCodes.NotFound, "not found"));

        await controller.HeadResource("no-such-thing");

        controller.Response.StatusCode.Should().Be(404);
        controller.Response.Headers.Should().NotContainKey(HttpHeaders.XPreservationResourceType);
    }

    [Fact]
    public async Task A_Tombstone_Failure_Gives_410_With_No_Type_Header()
    {
        var controller = ControllerAt("/repository/gone");
        A.CallTo(() => mediator.Send(A<GetResourceType>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<string?>(ErrorCodes.Tombstone, "Resource has been replaced by a Tombstone."));

        await controller.HeadResource("gone");

        controller.Response.StatusCode.Should().Be(410);
        controller.Response.Headers.Should().NotContainKey(HttpHeaders.XPreservationResourceType);
    }
}
