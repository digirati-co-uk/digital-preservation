using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Features.Import;

/// <summary>
/// Issue #444. If the importer dies during the commit request, Fedora still finishes the commit, so
/// failing the job would put the deposit into Error while the Archival Group has a new version. For
/// a stale job whose commit had started (<see cref="IImportJobResultStore.RecordCommitStarted"/>),
/// this waits for Fedora to finish the transaction, then decides the outcome from S3 and Fedora
/// (<see cref="CommitOutcomeVerifier"/>, issue #459) and records Completed or CompletedWithErrors.
///
/// Called only from the periodic sweep (<see cref="ImportJobReapService"/>), never at importer
/// start-up: a new importer starts while the old one may still be committing. The inline reap in
/// <see cref="IImportJobResultStore.GetActiveJobsForArchivalGroup"/> leaves these jobs alone.
/// </summary>
public class ImportJobCommitReconciler(
    IImportJobResultStore importJobResultStore,
    IFedoraClient fedoraClient,
    IOptions<ImportJobOptions> importJobOptions,
    ILogger<ImportJobCommitReconciler> logger,
    Func<TimeSpan, CancellationToken, Task>? delayOverride = null)
{
    private TimeSpan GiveUpAfter => TimeSpan.FromMinutes(importJobOptions.Value.CommitReconcileGiveUpMinutes);

    /// <returns>How many jobs were given a final result.</returns>
    public async Task<Result<int>> ReconcileStaleCommits(CancellationToken cancellationToken)
    {
        var staleJobs = await importJobResultStore.GetStaleCommittingJobs(cancellationToken);
        if (staleJobs.Failure)
        {
            return Result.Fail<int>(staleJobs.ErrorCode ?? ErrorCodes.UnknownError, staleJobs.ErrorMessage);
        }

        var reconciled = 0;
        foreach (var job in staleJobs.Value!)
        {
            try
            {
                if (await Reconcile(job, cancellationToken))
                {
                    reconciled++;
                }
            }
            catch (Exception e)
            {
                // One job's failure mustn't stop the sweep; it is tried again next time.
                logger.LogError(e, "Reconciling interrupted commit of import job {JobId} threw; will retry next sweep", job.Id);
            }
        }
        return Result.Ok(reconciled);
    }

    private async Task<bool> Reconcile(StaleCommittingJob job, CancellationToken cancellationToken)
    {
        var result = job.ImportJobResult;
        var archivalGroupPath = job.ImportJob.ArchivalGroup.GetPathUnderRoot()!;

        // Wait for Fedora to finish the transaction: a GET answers 404 or 410 once the commit is over
        // (404 is usual: Fedora removes a finished transaction soon after its expiry). Anything else -
        // 204 while it's still committing, an error from the request - means ask again next sweep. A
        // GET past the transaction's expiry blocks until the commit ends, which is the wait we want.
        bool finished;
        try
        {
            var status = await fedoraClient.GetTransactionHttpStatus(new Transaction { Location = job.CommitTransaction });
            finished = FedoraTransactionMonitor.IsTransactionFinished(status);
            if (!finished)
            {
                logger.LogInformation("Import job {JobId}: transaction {Transaction} still answers {Status}; will check again next sweep",
                    job.Id, job.CommitTransaction, (int)status);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Import job {JobId}: couldn't ask Fedora about transaction {Transaction}; will check again next sweep",
                job.Id, job.CommitTransaction);
            finished = false;
        }

        if (!finished)
        {
            if (DateTime.UtcNow - job.CommitStarted.ToUniversalTime() < GiveUpAfter)
            {
                return false;
            }
            return await Save(job, Failed(result,
                $"The importer stopped during the commit (started {job.CommitStarted:O}), and Fedora still hadn't finished " +
                $"transaction {job.CommitTransaction} after {GiveUpAfter}. The outcome could not be confirmed. " +
                CommitOutcomeVerifier.RunbookPointer), cancellationToken);
        }

        var verifier = new CommitOutcomeVerifier(fedoraClient, logger, delayOverride);
        var verification = await verifier.Verify(archivalGroupPath, job.ImportJob.IsUpdate, result.SourceVersion, result);
        var finalResult = verification.Outcome switch
        {
            CommitVerification.Confirmed => Completed(result, verification.NewVersion),
            CommitVerification.FedoraDisagrees => Failed(result,
                "The importer stopped during the commit, and Fedora finished it, but Fedora and S3 disagree: " + verification.Message,
                verification.NewVersion),
            CommitVerification.ConfirmedNotCommitted => Failed(result,
                "The importer stopped during the commit, and the commit did not happen: the Archival Group's version didn't change."),
            _ => Failed(result,
                "The importer stopped during the commit, and the outcome could not be confirmed: the Archival Group's version couldn't be read. " +
                CommitOutcomeVerifier.RunbookPointer)
        };
        return await Save(job, finalResult, cancellationToken);
    }

    private static ImportJobResult Completed(ImportJobResult result, string? newVersion)
    {
        result.Status = ImportJobStates.Completed;
        result.NewVersion = newVersion;
        result.DateFinished = DateTime.UtcNow;
        result.LastModified = result.DateFinished.Value;
        return result;
    }

    private static ImportJobResult Failed(ImportJobResult result, string message, string? newVersion = null)
    {
        result.Status = ImportJobStates.CompletedWithErrors;
        result.NewVersion = newVersion ?? result.SourceVersion;
        result.DateFinished = DateTime.UtcNow;
        result.LastModified = result.DateFinished.Value;
        result.Errors = [.. result.Errors ?? [], new Error { Message = message }];
        return result;
    }

    private async Task<bool> Save(StaleCommittingJob job, ImportJobResult finalResult, CancellationToken cancellationToken)
    {
        var saved = await importJobResultStore.SaveReconciledImportJobResult(job.Id, finalResult, cancellationToken);
        if (saved.Failure)
        {
            logger.LogError("Import job {JobId}: couldn't save the reconciled result: {CodeAndMessage}", job.Id, saved.CodeAndMessage());
            return false;
        }
        if (!saved.Value)
        {
            // The runner wrote its own result, or a heartbeat, after all: it was only paused.
            logger.LogInformation("Import job {JobId} was no longer stale when its reconciled result was saved; keeping its own record", job.Id);
            return false;
        }
        logger.LogWarning("Reconciled interrupted commit of import job {JobId} for {ArchivalGroup}: {Status}{Errors}",
            job.Id, job.ImportJob.ArchivalGroup, finalResult.Status,
            finalResult.Status == ImportJobStates.Completed ? "" : " - " + finalResult.Errors?.LastOrDefault()?.Message);
        return true;
    }
}
