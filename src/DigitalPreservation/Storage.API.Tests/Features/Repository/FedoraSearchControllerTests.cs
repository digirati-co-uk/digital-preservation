using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository;
using Storage.API.Features.Repository.Requests;

namespace Storage.API.Tests.Features.Repository;

/// <summary>
/// GET /FedoraSearch used to let page and pageSize through as null past the controller's guard
/// (both comparisons are false against null), reaching Postgres as LIMIT NULL - no limit at all
/// (#272). These prove defaults are applied before validation, and that the corrected messages
/// and bounds hold.
/// </summary>
public class FedoraSearchControllerTests
{
    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly FedoraSearchController controller;

    public FedoraSearchControllerTests()
    {
        controller = new FedoraSearchController(mediator);
    }

    [Fact]
    public async Task GetSimpleSearch_DefaultsPageAndPageSize_WhenNeitherSupplied()
    {
        SearchFromFedoraSimple? captured = null;
        A.CallTo(() => mediator.Send(A<SearchFromFedoraSimple>._, A<CancellationToken>._))
            .Invokes(call => captured = call.Arguments[0] as SearchFromFedoraSimple)
            .Returns(Task.FromResult(Result.OkNotNull<SearchCollectiveFedora?>(new SearchCollectiveFedora())));

        var result = await controller.GetSimpleSearch("a");

        Assert.IsType<OkObjectResult>(result.Result);
        captured.Should().NotBeNull();
        captured!.Page.Should().Be(0);
        captured.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetSimpleSearch_PassesExplicitPageAndPageSize_Unchanged()
    {
        SearchFromFedoraSimple? captured = null;
        A.CallTo(() => mediator.Send(A<SearchFromFedoraSimple>._, A<CancellationToken>._))
            .Invokes(call => captured = call.Arguments[0] as SearchFromFedoraSimple)
            .Returns(Task.FromResult(Result.OkNotNull<SearchCollectiveFedora?>(new SearchCollectiveFedora())));

        var result = await controller.GetSimpleSearch("a", page: 2, pageSize: 10);

        Assert.IsType<OkObjectResult>(result.Result);
        captured.Should().NotBeNull();
        captured!.Page.Should().Be(2);
        captured.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GetSimpleSearch_Accepts_PageSize500()
    {
        A.CallTo(() => mediator.Send(A<SearchFromFedoraSimple>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull<SearchCollectiveFedora?>(new SearchCollectiveFedora())));

        var result = await controller.GetSimpleSearch("a", pageSize: 500);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 501)]
    [InlineData(-1, null)]
    public async Task GetSimpleSearch_Returns400_ForInvalidPaging_AndSendsNothing(int? page, int? pageSize)
    {
        var result = await controller.GetSimpleSearch("a", page, pageSize);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        problem.Title.Should().Be("Invalid paging parameters");
        problem.Detail.Should().Contain("zero or more").And.Contain("between 1 and 500");
        A.CallTo(() => mediator.Send(A<SearchFromFedoraSimple>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
