using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Workspace;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Preservation.API.Features.Deposits;
using Preservation.API.Features.Deposits.Requests;


namespace Preservation.API.Tests.PreservationApiClient;

public class DepositsControllerTests
{
    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly WorkspaceManagerFactory workspaceFactory = A.Fake<WorkspaceManagerFactory>();
    private readonly DepositsController controller;

    public DepositsControllerTests()
    {
        controller = new DepositsController(
            A.Fake<ILogger<DepositsController>>(),
            mediator,
            workspaceFactory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task Active_ReturnsNoContent_OnSuccess()
    {
        const string depositId = "dep-1";
        A.CallTo(() => mediator.Send(
                A<ActiveDeposit>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Ok()));

        var result = await controller.Active(depositId);
        var objectResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, objectResult.StatusCode);
    }

    [Fact]
    public async Task Deactivate_ReturnsNoContent_OnSuccess()
    {
        const string depositId = "dep-2";
        A.CallTo(() => mediator.Send(
                A<ActiveDeposit>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Ok()));

        var result = await controller.Deactivate(depositId);
        var objectResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, objectResult.StatusCode);


    }

    [Fact]
    public async Task Active_ReturnsProblemDetails_OnFailure()
    {
        A.CallTo(() => mediator.Send(
                A<ActiveDeposit>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Fail(ErrorCodes.NotFound, "this failed somehow")));

        var result = await controller.Active("missing");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }


    [Fact]
    public async Task Deactivate_ReturnsProblemDetails_OnFailure()
    {
        A.CallTo(() => mediator.Send(
                A<ActiveDeposit>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Fail(ErrorCodes.NotFound, "this failed somehow")));

        var result = await controller.Deactivate("missing");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }
}
