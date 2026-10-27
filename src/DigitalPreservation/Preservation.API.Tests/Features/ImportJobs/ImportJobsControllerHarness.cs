using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using FakeItEasy.Configuration;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// The deposit/Archival Group statics, Mediator() and Controller() builders, and the Refusal()/
/// Executed()/Binary()/Container() helpers that ArchivalGroupMatchTests, InvalidSlugRefusalTests,
/// RenameRefusalTests, ResourcesWithinArchivalGroupTests, SuppressionGateTests and
/// DiffReferenceCaseInsensitivityTests each copied for themselves, about 50 lines a time (issue
/// #388 item 5.1) - including the EnableMetsIdNormalisation feature-flag value, which three of
/// those copies hardcoded "true" and two "false" despite none but SuppressionGateTests caring what
/// it is, now a single optional parameter defaulted to off.
/// </summary>
public static class ImportJobsControllerHarness
{
    public const string DepositId = "dep-1";
    public static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    public static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    public static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

    /// <summary>
    /// A mediator that knows one deposit, dep-1, for Archival Group cc/thing (or
    /// <paramref name="depositArchivalGroup"/>, if given), with no import jobs yet, and a default
    /// successful ExecuteImportJob - enough for the controller to get past its deposit checks to
    /// whatever is under test. Callers add or override further stubs on the same fake afterward
    /// (e.g. RenameRefusalTests stubs GetDiffImportJob on top of this for its diff-reference case).
    /// </summary>
    public static IMediator Mediator(Uri? depositArchivalGroup = null)
    {
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull<Deposit?>(new Deposit
            {
                Id = DepositUri,
                ArchivalGroup = depositArchivalGroup ?? ArchivalGroup,
                Files = DepositFiles
            }));
        A.CallTo(() => mediator.Send(A<GetImportJobResultsForDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<ImportJobResult>()));
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri(DepositUri + "/importjobs/results/job-1"),
                ImportJob = new Uri(DepositUri + "/importjobs/diff"),
                ArchivalGroup = ArchivalGroup,
                Status = ImportJobStates.Waiting
            }));
        return mediator;
    }

    /// <summary>
    /// A controller wired to <paramref name="mediator"/>, with FeatureFlags:EnableMetsIdNormalisation
    /// set per <paramref name="migrationFlagOn"/> (false by default; only SuppressionGateTests
    /// varies it, since it is the one check that reads the flag). <paramref name="requestPath"/>
    /// sets Request.Path (DiffReferenceCaseInsensitivityTests); <paramref name="urlHelper"/> is
    /// assigned to the controller's Url (RenameRefusalTests, for the diff route GetDiffUri builds).
    /// </summary>
    public static ImportJobsController Controller(
        IMediator mediator,
        bool migrationFlagOn = false,
        string? requestPath = null,
        IUrlHelper? urlHelper = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableMetsIdNormalisation"] = migrationFlagOn ? "true" : "false"
            }).Build();
        var mutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));
        var httpContext = new DefaultHttpContext();
        if (requestPath != null)
        {
            httpContext.Request.Path = new PathString(requestPath);
        }
        var controller = new ImportJobsController(
            NullLogger<ImportJobsController>.Instance, mediator, mutator, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        if (urlHelper != null)
        {
            controller.Url = urlHelper;
        }
        return controller;
    }

    /// <summary>A stub IUrlHelper whose RouteUrl always answers with the given diff job URI.</summary>
    public static IUrlHelper UrlHelperReturning(string diffJobUri)
    {
        var urlHelper = A.Fake<IUrlHelper>();
        A.CallTo(() => urlHelper.RouteUrl(A<UrlRouteContext>._)).Returns(diffJobUri);
        return urlHelper;
    }

    public static string? Refusal(IActionResult result)
    {
        var problem = result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(400);
        return problem.Detail;
    }

    public static IAssertConfiguration Executed(IMediator mediator) =>
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._));

    public static Binary Binary(string relativePath) =>
        new()
        {
            Id = new Uri($"{ArchivalGroup}/{relativePath}"),
            Origin = new Uri(DepositFiles, relativePath)
        };

    public static Container Container(string relativePath) =>
        new() { Id = new Uri($"{ArchivalGroup}/{relativePath}") };
}
