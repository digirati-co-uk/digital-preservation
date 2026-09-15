using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Features.Pipeline.Models;
using Pipeline.API.Features.Pipeline.Requests;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// Both endpoints on <see cref="PipelineController"/> take a caller-controlled deposit identifier
/// and use it to build a filesystem path. These pin the two fixes: DepositName is refused up front
/// on POST before anything is queued, and DepositId is refused - without touching the filesystem -
/// when it would resolve outside the configured file mount on GET. (A rooted value such as "/etc",
/// which Path.Combine would otherwise let discard the mount entirely, is covered at the PathX level
/// in PathXTests - on this controller it is refused earlier still, by slug character validation,
/// since none of '/', '\' or ':' are valid slug characters.)
/// </summary>
public class PipelineControllerTests : IDisposable
{
    private readonly string mountPath = Path.Combine(Path.GetTempPath(), "pipeline-controller-tests-" + Guid.NewGuid());
    private readonly IMediator mediator = A.Fake<IMediator>();

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
        A.CallTo(() => mediator.Send(A<LogPipelineJobStatus>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new LogPipelineStatusResult { Status = PipelineJobStates.Waiting }));
        A.CallTo(() => mediator.Send(A<ProcessPipelineJob>._, A<CancellationToken>._))
            .Returns(Result.Ok());

        return new PipelineController(
            mediator,
            NullLogger<PipelineController>.Instance,
            Options.Create(new StorageOptions { FileMountPath = mountPath }),
            A.Fake<IIdentityMinter>());
    }

    // --- POST: DepositName validation ---

    [Fact]
    public async Task A_Job_With_A_Valid_Deposit_Name_Is_Queued()
    {
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = "deposit-1" });

        result.Should().BeOfType<NoContentResult>();
        A.CallTo(() => mediator.Send(A<ProcessPipelineJob>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("deposit-1#/../..")]
    [InlineData("deposit 1")]
    public async Task A_Job_With_An_Invalid_Deposit_Name_Is_Refused_Before_Anything_Is_Queued(string depositName)
    {
        var controller = Controller();

        var result = await controller.ExecutePipelineJob(new PipelineJob { DepositName = depositName });

        result.Should().BeOfType<BadRequestObjectResult>();
        A.CallTo(() => mediator.Send(A<LogPipelineJobStatus>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<ProcessPipelineJob>._, A<CancellationToken>._)).MustNotHaveHappened();
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
