using System.Text;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Workspace;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Preservation.API.Features.Deposits;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.IIIF;


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
            workspaceFactory,
            A.Fake<ITokenService>())
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
                A<ActivateDeposit>._,
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
                A<ActivateDeposit>._,
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
                A<ActivateDeposit>._,
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
                A<ActivateDeposit>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Fail(ErrorCodes.NotFound, "this failed somehow")));

        var result = await controller.Deactivate("missing");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }
}

public class PostIIIFManifestToDepositTests
{
    private const string DepositId = "dep-1";
    private const string ValidManifestJson = """
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "id": "https://example.org/manifest/1",
          "type": "Manifest",
          "label": { "en": ["Test manifest"] }
        }
        """;

    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly ITokenService tokenService;
    private readonly DepositsController controller;

    public PostIIIFManifestToDepositTests()
    {
        tokenService = new TokenService(new MemoryCache(new MemoryCacheOptions()));
        controller = new DepositsController(
            A.Fake<ILogger<DepositsController>>(),
            mediator,
            A.Fake<WorkspaceManagerFactory>(),
            tokenService);
    }

    private void SetupRequest(string id, string token, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.org");
        context.Request.Path = $"/deposits/{id}/iiif-token/{token}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Fact]
    public async Task PostIIIFManifest_Returns401_WhenTokenNotInCache()
    {
        SetupRequest(DepositId, "unknown-token", ValidManifestJson);

        var result = await controller.PostIIIFManifestToDepositWithToken(DepositId, "unknown-token");

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task PostIIIFManifest_Returns401_WhenTokenBelongsToDifferentDeposit()
    {
        var token = tokenService.GetToken("someuser/deposit/dep-99");
        SetupRequest(DepositId, token, ValidManifestJson);

        var result = await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task PostIIIFManifest_Returns400_WhenBodyIsNotValidJson()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        SetupRequest(DepositId, token, "this is not json at all");

        var result = await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        problem.Title.Should().Be("Invalid IIIF Manifest");
    }

    [Fact]
    public async Task PostIIIFManifest_Returns204_WhenMediatorSucceeds()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        SetupRequest(DepositId, token, ValidManifestJson);
        A.CallTo(() => mediator.Send(
                A<UpdateLogicalStructMapsFromManifest>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Ok()));

        var result = await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task PostIIIFManifest_Returns404_WhenMediatorReportsNotFound()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        SetupRequest(DepositId, token, ValidManifestJson);
        A.CallTo(() => mediator.Send(
                A<UpdateLogicalStructMapsFromManifest>._,
                A<CancellationToken>._))
            .Returns(Task.FromResult(Result.Fail(ErrorCodes.NotFound, "deposit not found")));

        var result = await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PostIIIFManifest_PassesCorrectIiifBaseUrl_ToMediator()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        SetupRequest(DepositId, token, ValidManifestJson);

        UpdateLogicalStructMapsFromManifest? captured = null;
        A.CallTo(() => mediator.Send(
                A<UpdateLogicalStructMapsFromManifest>._,
                A<CancellationToken>._))
            .Invokes(call => captured = call.Arguments[0] as UpdateLogicalStructMapsFromManifest)
            .Returns(Task.FromResult(Result.Ok()));

        await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        captured.Should().NotBeNull();
        captured!.IiifBaseUrl.Should().Be($"https://example.org/deposits/{DepositId}/iiif/");
    }

    [Fact]
    public async Task PostIIIFManifest_PassesRawJson_ToMediator()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        SetupRequest(DepositId, token, ValidManifestJson);

        UpdateLogicalStructMapsFromManifest? captured = null;
        A.CallTo(() => mediator.Send(
                A<UpdateLogicalStructMapsFromManifest>._,
                A<CancellationToken>._))
            .Invokes(call => captured = call.Arguments[0] as UpdateLogicalStructMapsFromManifest)
            .Returns(Task.FromResult(Result.Ok()));

        await controller.PostIIIFManifestToDepositWithToken(DepositId, token);

        captured!.RawManifestJson.Should().Contain("\"type\": \"Manifest\"");
    }
}
