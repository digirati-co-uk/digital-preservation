using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// The diff-reference match (IsPostedDiffReference) used to compare the request path against the
/// job's Id with an ordinal, case-sensitive EndsWith. GetDiffUri lower-cases the diff URI it hands
/// back to callers, but ASP.NET routing is case-insensitive, so a client that POSTs to
/// /deposits/{id}/ImportJobs (the controller's own casing) with that lower-case id routed fine but
/// was silently not recognised as a diff reference - falling through to a confusing "Import job
/// must declare which Deposit it is for" 400 (issue #274 item 1).
/// </summary>
public class DiffReferenceCaseInsensitivityTests
{
    [Theory]
    [InlineData("/deposits/dep-1/ImportJobs", "https://preservation.test/deposits/dep-1/importjobs/diff")]
    [InlineData("/deposits/dep-1/importjobs", "https://preservation.test/deposits/dep-1/importjobs/diff")]
    [InlineData("/deposits/dep-1/importjobs", "https://preservation.test/deposits/dep-1/ImportJobs/Diff")]
    public async Task A_Diff_Reference_Is_Recognised_Regardless_Of_Casing(string path, string jobId)
    {
        var mediator = Mediator();
        var controller = Controller(mediator, path);

        await controller.ExecuteImportJob(DepositId, new ImportJob { Id = new Uri(jobId) }, TestContext.Current.CancellationToken);

        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task A_Diff_Id_With_A_Non_Empty_Operation_List_Is_Not_A_Diff_Reference()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, "/deposits/dep-1/ImportJobs");
        var job = new ImportJob { Id = new Uri("https://preservation.test/deposits/dep-1/importjobs/diff") };
        job.BinariesToAdd.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri($"{ArchivalGroup}/objects/new.pdf"),
            Origin = new Uri(DepositFiles, "objects/new.pdf")
        });

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The harness's default mediator, plus a failing GetDiffImportJob so the controller returns
    /// before calling GetDiffUri, which uses Url.RouteUrl - not wired up in a bare ControllerContext.
    /// </summary>
    private static IMediator Mediator()
    {
        var mediator = ImportJobsControllerHarness.Mediator();
        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<ImportJob>(DigitalPreservation.Common.Model.ErrorCodes.UnknownError, "not wired up for this test"));
        return mediator;
    }

    private static ImportJobsController Controller(IMediator mediator, string requestPath) =>
        ImportJobsControllerHarness.Controller(mediator, requestPath: requestPath);
}
