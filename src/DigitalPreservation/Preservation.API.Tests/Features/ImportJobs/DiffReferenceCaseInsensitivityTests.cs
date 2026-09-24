using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;

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
    private const string DepositId = "dep-1";
    private static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    private static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

    [Theory]
    [InlineData("/deposits/dep-1/ImportJobs", "https://preservation.test/deposits/dep-1/importjobs/diff")]
    [InlineData("/deposits/dep-1/importjobs", "https://preservation.test/deposits/dep-1/importjobs/diff")]
    [InlineData("/deposits/dep-1/importjobs", "https://preservation.test/deposits/dep-1/ImportJobs/Diff")]
    public async Task A_Diff_Reference_Is_Recognised_Regardless_Of_Casing(string path, string jobId)
    {
        var mediator = Mediator();
        var controller = Controller(mediator, path);

        await controller.ExecuteImportJob(DepositId, new ImportJob { Id = new Uri(jobId) }, default);

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

        await controller.ExecuteImportJob(DepositId, job, default);

        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A mediator that knows one deposit, dep-1, for Archival Group cc/thing, with no import jobs
    /// yet - enough for the controller to get past its deposit checks. GetDiffImportJob returns a
    /// failure so the controller returns before calling GetDiffUri, which uses Url.RouteUrl - not
    /// wired up in a bare ControllerContext.
    /// </summary>
    private static IMediator Mediator()
    {
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull<Deposit?>(new Deposit
            {
                Id = DepositUri,
                ArchivalGroup = ArchivalGroup,
                Files = DepositFiles
            }));
        A.CallTo(() => mediator.Send(A<GetImportJobResultsForDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<ImportJobResult>()));
        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<ImportJob>(ErrorCodes.UnknownError, "not wired up for this test"));
        return mediator;
    }

    private static ImportJobsController Controller(IMediator mediator, string requestPath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var mutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = new PathString(requestPath);
        return new ImportJobsController(
            NullLogger<ImportJobsController>.Instance, mediator, mutator, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }
}
