using System.Net;
using Amazon.S3;
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
        // No-op delay: these tests exercise the retry/backoff logic (call counts, which outcome
        // wins), never the real wait between attempts.
        new(importJobResultStore, NullLogger<ExecuteImportJobHandler>.Instance, fedoraClient,
            (_, _) => Task.CompletedTask);

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
            .Invokes(() => transaction.CommitStatusCode = HttpStatusCode.NoContent)
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
            .Invokes(() => transaction.CommitStatusCode = commitStatus)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Success.Should().BeTrue("the job result itself is still a successful outcome of running the job");
        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains(((int)commitStatus).ToString()));
        // The outcome is Fedora's: a transaction it has already acted on (successfully or not)
        // must not then be rolled back.
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
        // These are real answers from Fedora itself, unlike the ALB's own 502/503/504 below - no
        // verification needed, so the version must never be read (issue #383's second review,
        // point 1).
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(A<string>._, A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383's second review, point 1: the ALB's own ~4,000s idle timeout on a long commit
    /// arrives as an HTTP 504 response (502 for a dropped target connection), not an exception -
    /// CommitTransaction returns normally with the status recorded. Before this fix that non-2xx
    /// status failed the job immediately, with no verification, even though Fedora may already
    /// have committed - exactly the same gap as the connection-error path, just reached a
    /// different way.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_Transient_Gateway_Commit_Response_Confirmed_By_A_Later_Version_Gives_Completed(
        HttpStatusCode commitStatus)
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Invokes(() => transaction.CommitStatusCode = commitStatus)
            .Returns(Task.CompletedTask);
        // The transaction itself has already finished by the time this polls it.
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Transient_Gateway_Commit_Response_With_An_Unchanged_Version_Gives_CompletedWithErrors()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Invokes(() => transaction.CommitStatusCode = HttpStatusCode.GatewayTimeout)
            .Returns(Task.CompletedTask);
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("confirmed not committed"));
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383's second review, point 2: checking the Archival Group's version too early - while
    /// the transaction is still mid-commit - can read it before the commit lands and wrongly report
    /// "confirmed not committed". The fix polls the transaction itself first, while it answers 2xx
    /// or 409 (still committing, the same two codes FedoraTransactionMonitor uses), and only checks
    /// the version once it stops - here, on the third poll.
    /// </summary>
    [Fact]
    public async Task A_Connection_Error_Waits_For_The_Transaction_To_Finish_Before_Checking_The_Version()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction))
            .ReturnsNextFromSequence(HttpStatusCode.NoContent, HttpStatusCode.Conflict, HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null)).MustHaveHappened(1, Times.Exactly);
    }

    /// <summary>
    /// A transaction that never stops reporting as mid-commit must not be waited on forever - past
    /// the generous bound, this gives up as "could not be confirmed" without ever reading the
    /// Archival Group, rather than risk reading it while the commit may still be running.
    /// </summary>
    [Fact]
    public async Task A_Connection_Error_Gives_CouldNotBeConfirmed_When_The_Transaction_Never_Finishes()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.Conflict);

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("could not be confirmed"));
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(A<string>._, A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383's third review, point 1: polling the transaction to see if it has finished goes
    /// through the same ALB/NAT path as the commit PUT itself, so it can fail the same way. The
    /// previous version treated a failed poll as "finished" and went straight to the version check -
    /// the same false-positive the second review's fix closed for a transient gateway status,
    /// reopened for an outright exception. A failed poll must keep polling instead, exactly like a
    /// non-definitive status.
    /// </summary>
    [Fact]
    public async Task A_Connection_Error_Keeps_Polling_When_Checking_The_Transaction_Itself_Also_Fails()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction))
            .Throws(new HttpRequestException("connection reset")).Once()
            .Then.Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).MustHaveHappened(2, Times.Exactly);
    }

    /// <summary>
    /// Issue #383's second review, first nit: the backoff delay inside verification used to run
    /// against the job's own cancellation token. If a caller cancelled at this point - work may
    /// already be committed to Fedora - the resulting OperationCanceledException escaped all the
    /// way to the outer catch, which rolls back by default: the same post-commit DELETE issue #383's
    /// first review (point 3) set out to avoid. Passing an already-cancelled token must not change
    /// the outcome.
    /// </summary>
    [Fact]
    public async Task A_Connection_Error_Still_Verifies_And_Does_Not_Roll_Back_When_The_Jobs_Own_Token_Is_Cancelled()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();
        // Unlike CreateHandler's own no-op delay (which ignores whatever token it's given, so it
        // couldn't tell CancellationToken.None apart from a cancelled one), this one throws if
        // asked to wait with a cancelled token - proving the production code passes
        // CancellationToken.None to the backoff delay here, not the job's own (issue #383's second
        // review, first nit). If it didn't, the OperationCanceledException thrown below would
        // escape to the outer catch and roll back by default.
        var cancelAwareDelay = (TimeSpan _, CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var handler = new ExecuteImportJobHandler(
            importJobResultStore, NullLogger<ExecuteImportJobHandler>.Instance, fedoraClient, cancelAwareDelay);

        var result = await handler.Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), alreadyCancelled.Token);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("confirmed not committed"));
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383's second review, second nit: for a create - never an update, whose Archival
    /// Group already existed before the job started - a missing group after the transaction has
    /// already finished is genuine evidence the create never committed, not just an unconfirmed
    /// read. Distinct from <see cref="A_MonitorCancelled_Commit_Whose_Group_Is_Missing_Gives_CompletedWithErrors"/>,
    /// which simulates the general "read failed" case (a Result.Fail) rather than this specific,
    /// real-world one (GetCacheableStorageMap's uncaught S3 GetObject 404).
    /// </summary>
    [Fact]
    public async Task A_Create_Whose_Group_Does_Not_Exist_In_S3_Is_Confirmed_Not_Committed()
    {
        var transaction = new Transaction { Location = TxLocation };
        var createJob = new ImportJob
        {
            ArchivalGroup = ArchivalGroupUri,
            ArchivalGroupName = "Thing",
            CreatedBy = new Uri("https://preservation.test/agents/tester"),
            Deposit = new Uri("https://preservation.test/deposits/dep-1"),
            IsUpdate = false
        };
        A.CallTo(() => fedoraClient.BeginTransaction()).Returns(transaction);
        A.CallTo(() => fedoraClient.GetValidatedArchivalGroupForImportJob(ArchivalGroupPath, transaction))
            .Returns(Result.Ok<ArchivalGroup?>(null));
        A.CallTo(() => fedoraClient.CreateArchivalGroup(
                ArchivalGroupPath, A<string>._, "Thing", transaction, A<CancellationToken>._))
            .Returns(Result.OkNotNull<ArchivalGroup?>(new ArchivalGroup { Id = ArchivalGroupUri }));
        A.CallTo(() => importJobResultStore.SaveRunningImportJobResult(JobId, A<ImportJobResult>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(true));
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Throws(new AmazonS3Exception("Not found") { ErrorCode = "NoSuchKey", StatusCode = HttpStatusCode.NotFound });

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, createJob, InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("confirmed not committed"));
        // Genuine evidence once the transaction has finished, not an unconfirmed read - no need to
        // retry the rest of the backoff window.
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null)).MustHaveHappened(1, Times.Exactly);
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
        // Distinct from a read that never succeeded at all (see the "missing group" test below) -
        // this read succeeded every time, it just never showed the version moving (issue #383
        // review point 1d).
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("confirmed not committed"));
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
        // Polled the full backoff window (ExecuteImportJobHandler.CommitVerificationMaxAttempts)
        // rather than deciding from a single read (issue #383 review point 1).
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null)).MustHaveHappened(5, Times.Exactly);
    }

    /// <summary>
    /// Issue #383 review point 1: a commit still running when the monitor polls it is cancelled and
    /// then immediately reported failed, even though Fedora goes on to commit a few minutes later.
    /// The fix polls with backoff rather than deciding on one read, so a version that only shows up
    /// confirmed on a later attempt must still be reported Completed.
    /// </summary>
    [Fact]
    public async Task A_MonitorCancelled_Commit_Confirmed_Only_On_A_Later_Poll_Attempt_Gives_Completed()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        // First two reads see the pre-commit version (Fedora is still finishing); the third shows
        // the commit has landed.
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .ReturnsNextFromSequence(
                Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }),
                Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }),
                Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null)).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383 review point 3: GetArchivalGroupVersion throws rather than returning a Result.Fail
    /// for a group that genuinely doesn't exist in S3 yet (GetCacheableStorageMap's S3 read has no
    /// try/catch). The previous version's single, uncaught call let this exception escape all the
    /// way to the outer catch, which rolled back a transaction that had already been acted on and
    /// lost the specific "could not be confirmed" message. Verification must catch it and keep
    /// polling instead.
    /// </summary>
    [Fact]
    public async Task A_MonitorCancelled_Commit_Whose_Version_Check_Throws_Gives_CompletedWithErrors_Without_Rollback()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new OperationCanceledException());
        // Stands in for the real failure (GetCacheableStorageMap's uncatchable S3 GetObject for a
        // group whose inventory.json doesn't exist yet): any exception, not a Result.Fail.
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Throws(new InvalidOperationException("S3 GetObject failed: NoSuchKey (inventory.json)"));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("could not be confirmed"));
        // The whole point: a throw during verification must not fall through to the outer catch's
        // default rollback:true and DELETE a transaction Fedora has already acted on.
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Issue #383 review point 2: a connection error during the commit PUT itself (an
    /// HttpRequestException/IOException from the ALB's idle timeout or a dropped NAT mapping, not
    /// the monitor's own deliberate OperationCanceledException) used to fail the job with no
    /// verification at all - exactly the failure the safety net exists for.
    /// </summary>
    [Fact]
    public async Task A_Connection_Error_During_Commit_Confirmed_By_A_Later_Version_Gives_Completed()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        // A real, definitive answer that the transaction is gone (issue #383's third review: only
        // 404/410 counts - a GET never answers 409, and the fake's own unstubbed default (0) isn't
        // one either, so this must be stubbed for WaitForTransactionToFinish to ever return true).
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v2", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.Completed);
        A.CallTo(() => fedoraClient.RollbackTransaction(A<Transaction>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Connection_Error_During_Commit_With_An_Unchanged_Version_Gives_CompletedWithErrors()
    {
        var transaction = GivenTheHappyPathToCommit(sourceVersion: "v1");
        A.CallTo(() => fedoraClient.CommitTransaction(transaction, A<CancellationToken>._))
            .Throws(new HttpRequestException("connection reset"));
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(transaction)).Returns(HttpStatusCode.NotFound);
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

        var result = await CreateHandler().Handle(
            new ExecuteImportJob(JobId, UpdateJobWithNoChanges(), InitialResult()), CancellationToken.None);

        result.Value!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        result.Value!.Errors.Should().Contain(e => e.Message!.Contains("confirmed not committed"));
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
