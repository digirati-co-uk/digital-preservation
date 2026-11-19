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
/// GetImportJobsOrderedCollectionHandler used to check `Value: &gt; 0` as well as Success when
/// reading the total (issue #388 item 2.8's Diverged finding), so an empty-but-successful stream -
/// no import jobs at all - was reported as a 500 with a null Detail, instead of the empty
/// OrderedCollection the Preservation API's own copy of this logic already returned for the
/// identical condition. These pin the fix.
/// </summary>
public class GetImportJobsOrderedCollectionHandlerTests
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

    private static IImportJobResultStore StoreReturning(Result<int> totalItemsResult)
    {
        var store = A.Fake<IImportJobResultStore>();
        A.CallTo(() => store.GetTotalImportJobs(A<CancellationToken>._)).Returns(Task.FromResult(totalItemsResult));
        return store;
    }

    [Fact]
    public async Task An_Empty_But_Successful_Total_Returns_An_Empty_Collection_Not_A_Failure()
    {
        var handler = new GetImportJobsOrderedCollectionHandler(
            NullLogger<GetImportJobsOrderedCollectionHandler>.Instance,
            StoreReturning(Result.OkNotNull(0)),
            BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollection(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task A_Genuine_Failure_To_Read_The_Total_Is_Still_Reported_As_A_Failure()
    {
        var handler = new GetImportJobsOrderedCollectionHandler(
            NullLogger<GetImportJobsOrderedCollectionHandler>.Instance,
            StoreReturning(Result.FailNotNull<int>(ErrorCodes.UnknownError, "db unavailable")),
            BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollection(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("db unavailable");
    }

    [Fact]
    public async Task A_Nonempty_Total_Still_Builds_A_Real_Collection()
    {
        var handler = new GetImportJobsOrderedCollectionHandler(
            NullLogger<GetImportJobsOrderedCollectionHandler>.Instance,
            StoreReturning(Result.OkNotNull(5)),
            BuildConverters());

        var result = await handler.Handle(new GetImportJobsOrderedCollection(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(5);
    }
}
