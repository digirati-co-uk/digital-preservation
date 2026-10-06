using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Features.Pipeline.Models;
using Preservation.Client;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// POST /pipeline is a thin proxy over Preservation API's POST /deposits/{id}/pipeline (issue #231):
/// it used to queue a job directly, bypassing the default-bucket guard and the lock check that only
/// the Preservation API endpoint applied. These pin that it now forwards instead, and GET's path
/// containment is unaffected (DepositId is refused - without touching the filesystem - when it would
/// resolve outside the configured file mount). (A rooted value such as "/etc", which Path.Combine
/// would otherwise let discard the mount entirely, is covered at the PathX level in PathXTests - on
/// this controller it is refused earlier still, by slug character validation, since none of '/', '\'
/// or ':' are valid slug characters.)
/// </summary>
public class PipelineControllerTests : IDisposable
{
    private readonly string mountPath = Path.Combine(Path.GetTempPath(), "pipeline-controller-tests-" + Guid.NewGuid());
    private readonly IPreservationApiClient preservationApiClient = A.Fake<IPreservationApiClient>();

    public PipelineControllerTests()
    {
        Directory.CreateDirectory(Path.Combine(mountPath, "deposit-1", "objects"));
        File.WriteAllText(Path.Combine(mountPath, "deposit-1", "objects", "a.txt"), "content");
    }

    public void Dispose()
    {
        if (Directory.Exists(mountPath))
            Directory.Delete(mountPath, true);
    }

    private PipelineController Controller()
    {
        return new PipelineController(
            NullLogger<PipelineController>.Instance,
            Options.Create(new StorageOptions { FileMountPath = mountPath }),
            preservationApiClient);
    }

    // --- POST: proxies to the Preservation API ---

    [Fact]
    public async Task A_Job_With_A_Valid_Deposit_Name_Forwards_The_Fetched_Deposit_To_RunPipeline()
    {
        var deposit = new Deposit { Id = new Uri("https://preservation.test/deposits/deposit-1") };
        A.CallTo(() => preservationApiClient.GetDeposit("deposit-1", A<CancellationToken>._))
            .Returns(Result.Ok<Deposit?>(deposit));
        A.CallTo(() => preservationApiClient.RunPipeline(deposit, A<CancellationToken>._))
            .Returns(Result.Ok());
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = "deposit-1" });

        result.Should().BeOfType<NoContentResult>();
        A.CallTo(() => preservationApiClient.RunPipeline(deposit, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(ErrorCodes.BadRequest, 400)]
    [InlineData(ErrorCodes.Conflict, 409)]
    public async Task A_Failure_From_RunPipeline_Is_Passed_Back_With_The_Preservation_Apis_Status(
        string errorCode, int expectedStatus)
    {
        var deposit = new Deposit { Id = new Uri("https://preservation.test/deposits/deposit-1") };
        A.CallTo(() => preservationApiClient.GetDeposit("deposit-1", A<CancellationToken>._))
            .Returns(Result.Ok<Deposit?>(deposit));
        A.CallTo(() => preservationApiClient.RunPipeline(deposit, A<CancellationToken>._))
            .Returns(Result.Fail(errorCode, "refused by the Preservation API"));
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = "deposit-1" });

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(ErrorCodes.UnknownError, 500)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    public async Task A_Failure_Fetching_The_Deposit_That_Is_Not_NotFound_Keeps_Its_Status(
        string errorCode, int expectedStatus)
    {
        // A Preservation API outage or auth problem must not be misreported as a missing deposit.
        A.CallTo(() => preservationApiClient.GetDeposit("deposit-1", A<CancellationToken>._))
            .Returns(Result.Fail<Deposit>(errorCode, "the Preservation API could not be reached"));
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = "deposit-1" });

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(expectedStatus);
        A.CallTo(() => preservationApiClient.RunPipeline(A<Deposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task An_Unknown_Deposit_Returns_404_And_RunPipeline_Is_Never_Called()
    {
        A.CallTo(() => preservationApiClient.GetDeposit("no-such-deposit", A<CancellationToken>._))
            .Returns(Result.Fail<Deposit>(ErrorCodes.NotFound, "No resource at /deposits/no-such-deposit"));
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = "no-such-deposit" });

        result.Should().BeOfType<NotFoundObjectResult>();
        A.CallTo(() => preservationApiClient.RunPipeline(A<Deposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("deposit-1#/../..")]
    [InlineData("deposit 1")]
    public async Task A_Job_With_An_Invalid_Deposit_Name_Is_Refused_Before_Fetching_The_Deposit(string depositName)
    {
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = depositName });

        result.Should().BeOfType<BadRequestObjectResult>();
        A.CallTo(() => preservationApiClient.GetDeposit(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    // --- GET: DepositId path containment ---

    [Fact]
    public async Task An_Existing_Deposit_Folder_Is_Listed()
    {
        var controller = Controller();

        var result = await controller.CheckDepositFolderAndContents(new DepositFilesModel { DepositId = "deposit-1" });

        result.Errors.Should().BeEmpty();
        result.FilesInTarget.Should().Contain(f => f.EndsWith("a.txt"));
    }

    [Fact]
    public async Task A_DepositId_Containing_A_Slash_Is_Refused_By_Slug_Validation()
    {
        var controller = Controller();

        var result = await controller.CheckDepositFolderAndContents(new DepositFilesModel { DepositId = "../../.." });

        result.Errors.Should().NotBeEmpty();
        result.Directories.Should().BeNull();
        result.FilesInTarget.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task A_DepositId_Of_Dots_Alone_Is_Refused_By_The_Path_Containment_Check()
    {
        // ".." is, character by character, a valid slug - PreservedResource.ValidSlug checks each
        // character in isolation and does not special-case a dots-only segment, so slug validation
        // alone would let this through. The containment check (PathX.IsUnderRoot, applied after
        // Path.GetFullPath resolves the ".." lexically) is what actually refuses it here.
        var controller = Controller();

        var result = await controller.CheckDepositFolderAndContents(new DepositFilesModel { DepositId = ".." });

        result.Errors.Should().NotBeEmpty();
        result.Directories.Should().BeNull();
        result.FilesInTarget.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task An_Invalid_Slug_DepositId_Is_Refused_With_A_Clear_Reason()
    {
        var controller = Controller();

        var result = await controller.CheckDepositFolderAndContents(new DepositFilesModel { DepositId = "deposit/1" });

        result.Errors.Should().ContainSingle(e => e.Contains("not valid"));
    }
}
