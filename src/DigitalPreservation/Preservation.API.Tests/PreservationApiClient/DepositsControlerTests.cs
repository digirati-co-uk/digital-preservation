using System.Text;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Workspace;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ListDeposits_Returns400_WhenPageIsLessThanOne(int page)
    {
        var result = await controller.ListDeposits(new DepositQuery { Page = page });

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        problem.Title.Should().Be("Invalid paging parameters");
        A.CallTo(() => mediator.Send(A<GetDeposits>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    public async Task ListDeposits_Returns400_WhenPageSizeIsOutOfRange(int pageSize)
    {
        var result = await controller.ListDeposits(new DepositQuery { PageSize = pageSize });

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        problem.Title.Should().Be("Invalid paging parameters");
        A.CallTo(() => mediator.Send(A<GetDeposits>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ListDeposits_Returns200_WhenNoPagingParametersSupplied()
    {
        var page = new DepositQueryPage { Deposits = [], Page = 1, PageSize = 100, Total = 0 };
        A.CallTo(() => mediator.Send(A<GetDeposits>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull(page)));

        var result = await controller.ListDeposits(null);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<DepositQueryPage>(objectResult.Value);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(100);
    }

    // ---- POST deposits/{id}/mets/access: refused before the METS is touched ------------------

    private static readonly IOptions<AccessConditionProfile> StandardProfile = Options.Create(
        AccessConditionProfile.Simple([new AccessRestriction { Value = "Open", Label = "Open" }]));

    private void ADepositWithMets(string id, Uri? lockedBy = null) =>
        A.CallTo(() => mediator.Send(A<GetDeposit>.That.Matches(r => r.Id == id), A<CancellationToken>._))
            .Returns(Result.Ok<Deposit?>(new Deposit { MetsETag = "\"etag-1\"", LockedBy = lockedBy }));

    private static AccessChange Open(string path = "objects") =>
        new() { Path = path, AccessRestrictions = ["Open"] };

    [Fact]
    public async Task SetAccess_Without_The_METS_ETag_Is_A_Conflict()
    {
        ADepositWithMets("dep-access-1");

        var result = await controller.SetAccess("dep-access-1", Open(), StandardProfile);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SetAccess_With_A_Value_Outside_The_Vocabulary_Is_A_Bad_Request_That_Says_Why()
    {
        ADepositWithMets("dep-access-2");
        controller.Request.Headers.IfMatch = "\"etag-1\"";

        var result = await controller.SetAccess("dep-access-2",
            new AccessChange { Path = "objects", AccessRestrictions = ["Secret"] }, StandardProfile);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Detail.Should().Contain("'Secret' is not an access restriction here");
    }

    [Fact]
    public async Task SetAccess_On_A_Deposit_Someone_Else_Has_Locked_Is_A_Conflict()
    {
        ADepositWithMets("dep-access-3", lockedBy: new Uri("https://preservation.test/agents/someone-else"));
        controller.Request.Headers.IfMatch = "\"etag-1\"";

        var result = await controller.SetAccess("dep-access-3", Open(), StandardProfile);

        Assert.IsType<ObjectResult>(result).StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }
}

/// <summary>
/// GetDepositAsIIIFManifestWithToken shares the same IsValidForDeposit check and
/// IiifSessionExpiredResult response as MediaController's GetMedia (issue #388 item 2.9). No test
/// previously covered this endpoint's token check at all.
/// </summary>
public class GetDepositAsIIIFManifestWithTokenTests
{
    private const string DepositId = "dep-1";

    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly ITokenService tokenService = new TokenService(new MemoryCache(new MemoryCacheOptions()));
    private readonly DepositsController controller;

    public GetDepositAsIIIFManifestWithTokenTests()
    {
        controller = new DepositsController(
            A.Fake<ILogger<DepositsController>>(),
            mediator,
            A.Fake<WorkspaceManagerFactory>(),
            tokenService);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.org");
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Fact]
    public void Without_A_Redirect_It_Answers_With_The_Tokenised_Address()
    {
        // The Preservation UI asks this way: its client would follow a redirect, and have the API
        // build the whole manifest just to learn where it is.
        var result = controller.GetDepositAsIIIF(DepositId, redirect: false);

        var ok = Assert.IsType<OkObjectResult>(result);
        var id = ok.Value!.GetType().GetProperty("id")!.GetValue(ok.Value)!.ToString()!;
        id.Should().StartWith($"https://example.org/deposits/{DepositId}/iiif-token/");
        var token = id[(id.LastIndexOf('/') + 1)..];
        tokenService.IsValidForDeposit(token, DepositId).Should().BeTrue("it is a real token for this deposit");
        A.CallTo(() => mediator.Send(A<GetDepositAsIIIFManifest>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public void By_Default_It_Redirects()
    {
        controller.GetDepositAsIIIF(DepositId).Should().BeOfType<RedirectToRouteResult>();
    }

    [Fact]
    public async Task The_Manifest_Is_Served_As_Iiif_Presentation_3()
    {
        var token = tokenService.GetToken($"someuser/deposit/{DepositId}");
        A.CallTo(() => mediator.Send(A<GetDepositAsIIIFManifest>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new global::IIIF.Presentation.V3.Manifest { Id = "https://example.org/m" }));

        var result = await controller.GetDepositAsIIIFManifestWithToken(DepositId, token);

        Assert.IsType<ContentResult>(result).ContentType.Should()
            .Be(Preservation.API.IIIF.ManifestProjection.ManifestMediaType).And.StartWith("application/ld+json");
    }

    [Fact]
    public async Task Returns401_WhenTokenNotInCache()
    {
        var result = await controller.GetDepositAsIIIFManifestWithToken(DepositId, "unknown-token");

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        problem.Title.Should().Be("Session expired");
        A.CallTo(() => mediator.Send(A<GetDepositAsIIIFManifest>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Returns401_WhenTokenBelongsToADifferentDeposit()
    {
        var token = tokenService.GetToken("someuser/deposit/dep-99");

        var result = await controller.GetDepositAsIIIFManifestWithToken(DepositId, token);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        A.CallTo(() => mediator.Send(A<GetDepositAsIIIFManifest>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
