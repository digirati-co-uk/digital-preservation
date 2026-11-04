using System.Net;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Issue #347: ExecuteImportJobHandler performs ContainersToRename and BinariesToRename inside the
/// job's transaction, records them in ContainersRenamed / BinariesRenamed, and fails (rolling back)
/// if Fedora refuses one. Renames run after the patches, so a binary that is both patched and
/// renamed ends with its new name - PutBinary never changes an existing title.
/// </summary>
public class ExecuteImportJobRenameTests
{
    private const string JobId = "job-1";
    private const string ArchivalGroupPath = "cc/thing";
    private static readonly Uri ArchivalGroupUri = new("https://storage.test/repository/" + ArchivalGroupPath);

    private readonly IFedoraClient fedoraClient = A.Fake<IFedoraClient>();
    private readonly IImportJobResultStore importJobResultStore = A.Fake<IImportJobResultStore>();
    private readonly Transaction transaction = new() { Location = new Uri("https://storage.test/repository/fcr:tx/abc123") };

    private static Container Container(string path, string name) =>
        new() { Id = new Uri($"{ArchivalGroupUri}/{path}"), Name = name };

    private static Binary Binary(string path, string name) =>
        new() { Id = new Uri($"{ArchivalGroupUri}/{path}"), Name = name, Digest = "abc", ContentType = "image/tiff" };

    private static ImportJob UpdateJob() => new()
    {
        ArchivalGroup = ArchivalGroupUri,
        ArchivalGroupName = "Thing",
        CreatedBy = new Uri("https://preservation.test/agents/tester"),
        Deposit = new Uri("https://preservation.test/deposits/dep-1"),
        IsUpdate = true
    };

    private static ImportJobResult InitialResult() => new()
    {
        Id = new Uri("https://storage.test/import/results/" + JobId),
        ImportJob = new Uri("https://storage.test/import/" + JobId),
        ArchivalGroup = ArchivalGroupUri,
        Status = ImportJobStates.Waiting
    };

    public ExecuteImportJobRenameTests()
    {
        A.CallTo(() => fedoraClient.BeginTransaction()).Returns(transaction);
        A.CallTo(() => fedoraClient.GetValidatedArchivalGroupForImportJob(ArchivalGroupPath, transaction))
            .Returns(Result.OkNotNull<ArchivalGroup?>(new ArchivalGroup
            {
                Id = ArchivalGroupUri,
                Version = new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }
            }));
        A.CallTo(() => importJobResultStore.SaveRunningImportJobResult(JobId, A<ImportJobResult>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(true));
        A.CallTo(() => fedoraClient.UpdateContainerMetadata(
                ArchivalGroupPath, A<string?>._, A<string>.Ignored, transaction, A<CancellationToken>._))
            .Returns(Result.Ok());
        A.CallTo(() => fedoraClient.Rename(A<PreservedResource>._, A<string>.Ignored, transaction, A<CancellationToken>._))
            .ReturnsLazily((PreservedResource resource, string _, Transaction _, CancellationToken _) =>
                Result.OkNotNull(resource));
        A.CallTo(() => fedoraClient.PutBinary(A<Binary>._, A<string>.Ignored, transaction, A<CancellationToken>._))
            .ReturnsLazily((Binary binary, string _, Transaction _, CancellationToken _) =>
                Result.OkNotNull<Binary?>(binary));
        A.CallTo(() => fedoraClient.CreateContainerWithinArchivalGroup(
                A<string>.Ignored, A<string>.Ignored, A<string?>._, transaction, A<CancellationToken>._))
            .ReturnsLazily((string path, string _, string? name, Transaction? _, CancellationToken _) =>
                Result.OkNotNull<Container?>(new Container { Id = new Uri($"https://storage.test/repository/{path}"), Name = name }));
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Invokes(() => transaction.CommitStatusCode = HttpStatusCode.NoContent)
            .Returns(Task.CompletedTask);
    }

    private Task<Result<ImportJobResult>> Execute(ImportJob job) =>
        new ExecuteImportJobHandler(importJobResultStore, NullLogger<ExecuteImportJobHandler>.Instance, fedoraClient,
                (_, _) => Task.CompletedTask)
            .Handle(new ExecuteImportJob(JobId, job, InitialResult()), CancellationToken.None);

    [Fact]
    public async Task Container_Renames_Are_Performed_And_Recorded()
    {
        var job = UpdateJob();
        var objects = Container("objects", "Digitised objects");
        var folder = Container("objects/folder", "Folder one");
        job.ContainersToRename.AddRange([objects, folder]);

        var result = await Execute(job);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        result.Value.ContainersRenamed.Should().Equal(objects, folder);
        A.CallTo(() => fedoraClient.Rename(objects, "tester", transaction, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fedoraClient.Rename(folder, "tester", transaction, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Binary_Renames_Are_Performed_And_Recorded()
    {
        var job = UpdateJob();
        var page = Binary("objects/page-001.tif", "Page one");
        job.BinariesToRename.Add(page);

        var result = await Execute(job);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        result.Value.BinariesRenamed.Should().ContainSingle().Which.Should().BeSameAs(page);
        A.CallTo(() => fedoraClient.Rename(page, "tester", transaction, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // A rename changes only the title: never a content PUT.
        A.CallTo(() => fedoraClient.PutBinary(A<Binary>._, A<string>.Ignored, A<Transaction>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_A_Rename_Plus_Adds_And_Patches_Does_All_Of_Them_With_Renames_After_Patches()
    {
        var job = UpdateJob();
        var newFolder = Container("objects/new-folder", "New folder");
        var added = Binary("objects/new-folder/page-002.tif", "Page two");
        var patchedAndRenamed = Binary("objects/page-001.tif", "Page one (rescanned)");
        var renamedFolder = Container("objects", "Digitised objects");
        job.ContainersToAdd.Add(newFolder);
        job.BinariesToAdd.Add(added);
        job.BinariesToPatch.Add(patchedAndRenamed);
        job.BinariesToRename.Add(patchedAndRenamed);
        job.ContainersToRename.Add(renamedFolder);

        var result = await Execute(job);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        result.Value.ContainersAdded.Should().ContainSingle();
        result.Value.BinariesAdded.Should().ContainSingle();
        result.Value.BinariesPatched.Should().ContainSingle();
        result.Value.BinariesRenamed.Should().ContainSingle();
        result.Value.ContainersRenamed.Should().ContainSingle();
        A.CallTo(() => fedoraClient.PutBinary(patchedAndRenamed, A<string>.Ignored, transaction, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => fedoraClient.Rename(patchedAndRenamed, A<string>.Ignored, transaction, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task A_Rename_Fedora_Refuses_Fails_The_Job_And_Rolls_Back()
    {
        var job = UpdateJob();
        var page = Binary("objects/page-001.tif", "Page one");
        job.BinariesToRename.Add(page);
        A.CallTo(() => fedoraClient.Rename(page, A<string>.Ignored, transaction, A<CancellationToken>._))
            .Returns(Result.FailNotNull<PreservedResource>(ErrorCodes.Conflict, "Rename of cc/thing/objects/page-001.tif failed"));

        var result = await Execute(job);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value.Errors.Should().Contain(e => e.Message!.Contains("cc/thing/objects/page-001.tif"));
        result.Value.BinariesRenamed.Should().BeEmpty();
        A.CallTo(() => fedoraClient.RollbackTransaction(transaction)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fedoraClient.CommitTransaction(A<Transaction>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Rename_To_An_Empty_Name_Is_Refused_Before_Fedora_Is_Touched()
    {
        var job = UpdateJob();
        job.ContainersToRename.Add(Container("objects", ""));

        var result = await Execute(job);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        A.CallTo(() => fedoraClient.BeginTransaction()).MustNotHaveHappened();
    }
}
