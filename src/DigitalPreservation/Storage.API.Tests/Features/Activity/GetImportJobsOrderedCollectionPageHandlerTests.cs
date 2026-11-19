using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Activity.Requests;
using Storage.API.Features.Import;

namespace Storage.API.Tests.Features.Activity;

/// <summary>
/// GetImportJobsOrderedCollectionPageHandler shared the same two Diverged bugs as its collection
/// counterpart (issue #388 item 2.8): it checked `Value: &gt; 0` as well as Success, turning an
/// empty-but-successful stream into a 500 with a null Detail, and it never rejected a page number
/// below 1. These pin both fixes.
/// </summary>
public class GetImportJobsOrderedCollectionPageHandlerTests
{
    private static Converters BuildConverters()
    {
        var fedoraOptions = Options.Create(new FedoraOptions
        {
            Root = new Uri("http://fedora.test/fcrepo/rest/"),
            AdminUser = "admin",
            AdminPassword = "test-password",
            Bucket = "fedora-bucket",
            OcflS3Prefix = string.Empty
        });
        var converterOptions = Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") });
        return new Converters(fedoraOptions, converterOptions);
    }

    private static IImportJobResultStore StoreWith(
        int totalItems, List<DigitalPreservation.Common.Model.ChangeDiscovery.Activity>? pageItems = null)
    {
        var store = A.Fake<IImportJobResultStore>();
        A.CallTo(() => store.GetTotalImportJobs(A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull(totalItems)));
        A.CallTo(() => store.GetActivityPageOfResults(A<int>._, A<int>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull(pageItems ?? [])));
        return store;
    }

    [Fact]
    public async Task An_Empty_But_Successful_Total_Returns_An_Empty_Page_Not_A_Failure()
    {
        var handler = new GetImportJobsOrderedCollectionPageHandler(
            NullLogger<GetImportJobsOrderedCollectionPageHandler>.Instance, StoreWith(totalItems: 0), BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollectionPage(1), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.OrderedItems.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_Page_Below_1_Is_Refused_As_A_Bad_Request(int page)
    {
        var handler = new GetImportJobsOrderedCollectionPageHandler(
            NullLogger<GetImportJobsOrderedCollectionPageHandler>.Instance, StoreWith(totalItems: 5), BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollectionPage(page), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }

    [Fact]
    public async Task A_Genuine_Failure_To_Read_The_Total_Is_Still_Reported_As_A_Failure()
    {
        var store = A.Fake<IImportJobResultStore>();
        A.CallTo(() => store.GetTotalImportJobs(A<CancellationToken>._))
            .Returns(Task.FromResult(Result.FailNotNull<int>(ErrorCodes.UnknownError, "db unavailable")));
        var handler = new GetImportJobsOrderedCollectionPageHandler(
            NullLogger<GetImportJobsOrderedCollectionPageHandler>.Instance, store, BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollectionPage(1), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("db unavailable");
    }
}
