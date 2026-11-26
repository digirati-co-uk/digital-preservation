using System.Net;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Fedora.Vocab;
using Storage.API.Features.Import;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Issue #444: a job whose importer died during the commit is reconciled from Fedora and S3 once the
/// transaction has finished, instead of being failed while its Archival Group has a new version.
/// </summary>
public class ImportJobCommitReconcilerTests
{
    private const string ArchivalGroupPath = "cc/thing";
    private static readonly Uri ArchivalGroupUri = new("https://storage.test/repository/" + ArchivalGroupPath);
    private static readonly Uri TxLocation = new("https://fedora.test/fcrepo/rest/fcr:tx/abc");

    private readonly IImportJobResultStore store = A.Fake<IImportJobResultStore>();
    private readonly IFedoraClient fedoraClient = A.Fake<IFedoraClient>();
    private ImportJobResult? saved;

    public ImportJobCommitReconcilerTests()
    {
        ImportFakes.FedoraAgreesWithS3(fedoraClient);
        A.CallTo(() => store.SaveReconciledImportJobResult(A<string>._, A<ImportJobResult>._, A<CancellationToken>._))
            .ReturnsLazily((string _, ImportJobResult result, CancellationToken _) =>
            {
                saved = result;
                return Result.OkNotNull(true);
            });
    }

    private ImportJobCommitReconciler CreateReconciler(int giveUpMinutes = 12 * 60) => new(
        store, fedoraClient, Options.Create(new ImportJobOptions { CommitReconcileGiveUpMinutes = giveUpMinutes }),
        NullLogger<ImportJobCommitReconciler>.Instance, (_, _) => Task.CompletedTask);

    private void GivenAStaleCommittingJob(bool isUpdate = true, DateTime? commitStarted = null, Action<ImportJobResult>? changes = null)
    {
        var result = new ImportJobResult
        {
            Id = new Uri("https://storage.test/import/results/job-1"),
            ImportJob = new Uri("https://storage.test/import/job-1"),
            ArchivalGroup = ArchivalGroupUri,
            Status = ImportJobStates.Running,
            SourceVersion = isUpdate ? "v1" : null
        };
        changes?.Invoke(result);
        var job = new StaleCommittingJob("job-1",
            new ImportJob { ArchivalGroup = ArchivalGroupUri, IsUpdate = isUpdate },
            result, commitStarted ?? DateTime.UtcNow.AddMinutes(-15), TxLocation);
        A.CallTo(() => store.GetStaleCommittingJobs(A<CancellationToken>._)).Returns(Result.OkNotNull(new List<StaleCommittingJob> { job }));
    }

    private void GivenTheTransactionAnswers(HttpStatusCode status) =>
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(A<Transaction>.That.Matches(t => t.Location == TxLocation)))
            .Returns(status);

    private void GivenS3Shows(string version) =>
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(ArchivalGroupPath, null))
            .Returns(Result.OkNotNull(new ObjectVersion { OcflVersion = version, MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }));

    [Fact]
    public async Task A_Commit_That_Landed_Is_Recorded_Completed_With_Its_New_Version()
    {
        GivenAStaleCommittingJob();
        GivenTheTransactionAnswers(HttpStatusCode.NotFound);
        GivenS3Shows("v2");

        var reconciled = await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        reconciled.Value.Should().Be(1);
        saved!.Status.Should().Be(ImportJobStates.Completed);
        saved.NewVersion.Should().Be("v2");
        saved.Errors.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task A_Create_That_Landed_Is_Recorded_Completed()
    {
        GivenAStaleCommittingJob(isUpdate: false);
        GivenTheTransactionAnswers(HttpStatusCode.Gone);
        GivenS3Shows("v1");

        await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        saved!.Status.Should().Be(ImportJobStates.Completed);
        saved.NewVersion.Should().Be("v1");
    }

    [Fact]
    public async Task A_Transaction_Still_Committing_Is_Left_For_The_Next_Sweep()
    {
        GivenAStaleCommittingJob();
        GivenTheTransactionAnswers(HttpStatusCode.NoContent);

        var reconciled = await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        reconciled.Value.Should().Be(0);
        A.CallTo(() => store.SaveReconciledImportJobResult(A<string>._, A<ImportJobResult>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => fedoraClient.GetArchivalGroupVersion(A<string?>._, A<Transaction?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Transaction_That_Cannot_Be_Asked_About_Is_Left_For_The_Next_Sweep()
    {
        GivenAStaleCommittingJob();
        A.CallTo(() => fedoraClient.GetTransactionHttpStatus(A<Transaction>._)).Throws(new HttpRequestException("timeout"));

        var reconciled = await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        reconciled.Value.Should().Be(0);
        A.CallTo(() => store.SaveReconciledImportJobResult(A<string>._, A<ImportJobResult>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Transaction_Still_Committing_After_The_Give_Up_Time_Is_Recorded_As_Unconfirmed()
    {
        GivenAStaleCommittingJob(commitStarted: DateTime.UtcNow.AddHours(-3));
        GivenTheTransactionAnswers(HttpStatusCode.NoContent);

        await CreateReconciler(giveUpMinutes: 120).ReconcileStaleCommits(CancellationToken.None);

        saved!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        saved.Errors!.Last().Message.Should().Contain("could not be confirmed").And.Contain("wedged-archival-group.md");
    }

    [Fact]
    public async Task A_Commit_That_Did_Not_Happen_Is_Recorded_With_Errors()
    {
        GivenAStaleCommittingJob();
        GivenTheTransactionAnswers(HttpStatusCode.NotFound);
        GivenS3Shows("v1");

        await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        saved!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        saved.NewVersion.Should().Be("v1");
        saved.Errors!.Last().Message.Should().Contain("did not happen");
    }

    [Fact]
    public async Task A_Commit_S3_Has_But_Fedora_Does_Not_Is_Recorded_With_Errors_Pointing_At_The_Runbook()
    {
        GivenAStaleCommittingJob(changes: r =>
            r.BinariesAdded.Add(new Binary { Id = new Uri(ArchivalGroupUri + "/objects/a.txt"), Name = "a.txt" }));
        GivenTheTransactionAnswers(HttpStatusCode.NotFound);
        GivenS3Shows("v2");
        A.CallTo(() => fedoraClient.GetResourceType("cc/thing/objects/a.txt", null))
            .Returns(Result.Fail<string?>(ErrorCodes.NotFound, "not found"));

        await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        saved!.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        saved.NewVersion.Should().Be("v2", "the version exists in OCFL, even though Fedora doesn't have it");
        saved.Errors!.Last().Message.Should().Contain("cc/thing/objects/a.txt").And.Contain("wedged-archival-group.md");
    }

    [Fact]
    public async Task A_Deletion_Fedora_Already_Has_Agrees()
    {
        GivenAStaleCommittingJob(changes: r =>
            r.BinariesDeleted.Add(new Binary { Id = new Uri(ArchivalGroupUri + "/objects/old.txt"), Name = "old.txt" }));
        GivenTheTransactionAnswers(HttpStatusCode.NotFound);
        GivenS3Shows("v2");
        A.CallTo(() => fedoraClient.GetResourceType("cc/thing/objects/old.txt", null))
            .Returns(Result.OkNotNull<string?>(RepositoryTypes.Tombstone));

        await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        saved!.Status.Should().Be(ImportJobStates.Completed);
    }

    [Fact]
    public async Task A_Job_Whose_Runner_Came_Back_Is_Not_Counted()
    {
        GivenAStaleCommittingJob();
        GivenTheTransactionAnswers(HttpStatusCode.NotFound);
        GivenS3Shows("v2");
        A.CallTo(() => store.SaveReconciledImportJobResult(A<string>._, A<ImportJobResult>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(false));

        var reconciled = await CreateReconciler().ReconcileStaleCommits(CancellationToken.None);

        reconciled.Value.Should().Be(0);
    }
}
