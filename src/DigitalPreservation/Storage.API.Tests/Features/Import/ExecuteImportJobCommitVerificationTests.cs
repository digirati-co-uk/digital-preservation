using System.Net;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Issue #350: FedoraClient.CommitTransaction stores the commit PUT's status but never throws for
/// a non-2xx response, and the monitor cancelling that PUT (because it found the transaction gone
/// on a post-commit poll) used to be treated as a plain success. These pin ExecuteImportJobHandler's
/// fix: only a 2xx commit is a plain success; a monitor-cancelled commit's outcome must be
/// confirmed against the Archival Group's own version before it can be reported as Completed; and
/// a transaction the pre-commit keep-alive has already found lost must stop the import loop before
/// it reaches a commit at all.
/// </summary>
public class ExecuteImportJobCommitVerificationTests
{
    private const string JobId = "job-1";
    private const string ArchivalGroupPath = "cc/thing";
    private static readonly Uri ArchivalGroupUri = new("https://storage.test/repository/" + ArchivalGroupPath);
    private static readonly Uri TxLocation = new("https://storage.test/repository/fcr:tx/abc123");

    private readonly IFedoraClient fedoraClient = A.Fake<IFedoraClient>();
    private readonly IImportJobResultStore importJobResultStore = A.Fake<IImportJobResultStore>();

    /// <summary>
    /// An update to an existing Archival Group with nothing to add, patch or delete - the simplest
    /// shape that reaches the commit, so these tests exercise only what happens around the commit
    /// itself, not the resource loop.
    /// </summary>
    private static ImportJob UpdateJobWithNoChanges() => new()
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

    private static ArchivalGroup ExistingGroupAtVersion(string ocflVersion) => new()
    {
        Id = ArchivalGroupUri,
        Version = new ObjectVersion { OcflVersion = ocflVersion, MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }
    };

    private ExecuteImportJobHandler CreateHandler() =>
        new(importJobResultStore, NullLogger<ExecuteImportJobHandler>.Instance, fedoraClient);

    /// <summary>
    /// Sets up every Fedora/store call the happy path to the commit needs, for an update with no
    /// resource changes. Each test then overrides CommitTransaction and/or GetArchivalGroupVersion.
    /// </summary>
    private Transaction GivenTheHappyPathToCommit(string sourceVersion = "v1")
    {
        var transaction = new Transaction { Location = TxLocation };
        A.CallTo(() => fedoraClient.BeginTransaction()).Returns(transaction);
        A.CallTo(() => fedoraClient.GetValidatedArchivalGroupForImportJob(ArchivalGroupPath, transaction))
            .Returns(Result.OkNotNull<ArchivalGroup?>(ExistingGroupAtVersion(sourceVersion)));
        A.CallTo(() => importJobResultStore.SaveRunningImportJobResult(JobId, A<ImportJobResult>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(true));
        // IsUpdate is true for every job in this file, so the handler always reaches this call
        // before the commit.
        A.CallTo(() => fedoraClient.UpdateContainerMetadata(
                ArchivalGroupPath, A<string?>._, A<string>.Ignored, transaction, A<CancellationToken>._))
            .Returns(Result.Ok());
        return transaction;
    }

    [Fact]
    public async Task A_Commit_Returning_204_Gives_Completed()
    {
        var transaction = GivenTheHappyPathToCommit();
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Invokes(() => transaction.StatusCode = HttpStatusCode.NoContent)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_Commit_Returning_A_NonSuccess_Status_Gives_CompletedWithErrors_And_Does_Not_Roll_Back(
        HttpStatusCode commitStatus)
    {
        var transaction = GivenTheHappyPathToCommit();
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Invokes(() => transaction.StatusCode = commitStatus)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Success.Should().BeTrue("the job result itself is still a successful outcome of running the job");
        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains(((int)commitStatus).ToString()));
        // The outcome is Fedora's: a transaction it has already acted on (successfully or not)
        // must not then be rolled back.
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The monitor cancelled the commit PUT (simulated here the same way FedoraTransactionMonitor
    /// itself does: CommitTransaction throwing OperationCanceledException, which it catches and
    /// turns into tx.Cancelled), but the group's version has genuinely moved past SourceVersion -
    /// the commit did happen, just invisibly to the cancelled PUT.
    /// </summary>
    [Fact]
    public async Task A_MonitorCancelled_Commit_Confirmed_By_A_Later_Version_Gives_Completed()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_MonitorCancelled_Commit_With_An_Unchanged_Version_Gives_CompletedWithErrors()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("could not be confirmed"));
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_MonitorCancelled_Commit_Whose_Group_Is_Missing_Gives_CompletedWithErrors()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.FailNotNull<ObjectVersion>(ErrorCodes.NotFound, "not found"));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("could not be confirmed"));
    }

    /// <summary>
    /// The pre-commit keep-alive (FedoraTransactionMonitor, tested separately) is what actually
    /// sets TransactionLost against a real ticking timer; this sets it directly to pin what the
    /// import loop does once it's set, without waiting on a real 60-second timer tick.
    /// </summary>
    [Fact]
    public async Task A_Transaction_Already_Lost_Before_Commit_Fails_Early_With_No_Further_Binary_Puts()
    {
        var transaction = GivenTheHappyPathToCommit();
        var job = UpdateJobWithNoChanges();
        var first = new Binary { Id = new Uri(ArchivalGroupUri + "/objects/a.tif") };
        var second = new Binary { Id = new Uri(ArchivalGroupUri + "/objects/b.tif") };
        job.BinariesToAdd.Add(first);
        job.BinariesToAdd.Add(second);

        A.CallTo(() => fedoraClient.PutBinary(
                A<Binary>.That.Matches(b => b.Id == first.Id), A<string>._, transaction, A<CancellationToken>._))
            .Invokes(() => transaction.TransactionLost = true)
            .Returns(Result.OkNotNull<Binary?>(first));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, job, InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("lost"));
        A.CallTo(() => fedoraClient.PutBinary(
                A<Binary>.That.Matches(b => b.Id == second.Id), A<string>._, A<Transaction>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => fedoraClient.CommitTransaction(A<Transaction>._, A<CancellationToken>._)).MustNotHaveHappened();
        // The transaction is already gone; rolling it back is pointless and must not be attempted.
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }
}
