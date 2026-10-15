using Storage.API.Fedora.Model;
using System.Diagnostics;
using System.Net;
using Amazon.S3;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Import.Requests;

public class ExecuteImportJob(string jobIdentifier, ImportJob importJob, ImportJobResult initialImportJobResult) : IRequest<Result<ImportJobResult>>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public ImportJob ImportJob { get; } = importJob;
    public ImportJobResult InitialImportJobResult { get; } = initialImportJobResult;
}

public class ExecuteImportJobHandler(
    IImportJobResultStore importJobResultStore,
    ILogger<ExecuteImportJobHandler> logger,
    IFedoraClient fedoraClient,
    Func<TimeSpan, CancellationToken, Task>? delayOverride = null) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
    // Seam for tests to skip the real wait while still exercising the retry count/logic - the
    // built-in DI container falls back to this default for any caller that doesn't supply one.
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delayOverride ?? Task.Delay;

    /// <summary>
    /// How many times <see cref="Handle"/>'s commit-outcome verification re-reads the Archival
    /// Group's version before giving up (issue #383 review point 1d). Exponential backoff between
    /// attempts (2s, 4s, 8s, 16s - about 30s total) gives Fedora time to actually finish a commit
    /// that was still running when the PUT's own response became untrustworthy.
    /// </summary>
    private const int CommitVerificationMaxAttempts = 5;

    /// <summary>
    /// Upper bound for how long to poll the transaction itself, waiting for it to stop reporting as
    /// mid-commit, before giving up on verifying a commit whose own response couldn't be trusted (a
    /// connection error, or an ALB 502/503/504 - issue #383's second review). Generous because
    /// nothing rules out Fedora-side taking a very long time to finish; giving up too early risks
    /// <see cref="VerifyCommitOutcome"/> reading the Archival Group before the commit has landed and
    /// wrongly reporting "confirmed not committed".
    /// </summary>
    private static readonly TimeSpan TransactionFinishMaxWait = TimeSpan.FromHours(1);

    private static readonly TimeSpan TransactionFinishPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Distinguishes "the Archival Group's version genuinely never moved" from "the check itself
    /// never got a trustworthy read" (issue #383 review point 1d) - collapsing these into one bool,
    /// as the pre-review version did, reported a transient read failure exactly the same way as a
    /// real, checked non-commit.
    /// </summary>
    private enum CommitVerification
    {
        Confirmed,
        ConfirmedNotCommitted,
        CouldNotConfirm
    }

    private static bool IsTransientGatewayStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public async Task<Result<ImportJobResult>> Handle(ExecuteImportJob request, CancellationToken cancellationToken)
    {
        var importJob = request.ImportJob;
        var callerIdentity = importJob.CreatedBy!.GetSlug()!.UnEscapeFromUri();
        var archivalGroupPathUnderRoot = importJob.ArchivalGroup.GetPathUnderRoot()!;
        logger.LogInformation("Executing Import Job");
        
        var start = DateTime.UtcNow;
        
        var importJobResult = request.InitialImportJobResult;
        importJobResult.Status = ImportJobStates.Running;
        importJobResult.DateBegun = start;
        importJobResult.LastModified = start;

        var preProcessValidationResult = PreProcessValidateImportJob(importJob);
        if (preProcessValidationResult.Failure)
        {
            importJobResult.Errors = [new Error { Message = preProcessValidationResult.ErrorMessage ?? "" }];
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }
        
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        
        var transaction = await fedoraClient.BeginTransaction();
        // Disposed on every path out of this method, including every FailEarly return - and always
        // after the explicit timer.DisposeAsync() calls below, since a callback parked at one of its
        // awaits keeps running after the timer is disposed and would otherwise race Dispose() here.
        using var transactionMonitor = new FedoraTransactionMonitor(logger, fedoraClient, transaction, stopwatch);
        var timer = new Timer(transactionMonitor.MaintainTransactionState, transaction, 60 * 1000, 60 * 1000);

        // From here to the commit, nothing may let an exception escape: the keep-alive timer would go
        // on ticking against an abandoned transaction, and its async void callback would take the whole
        // process down the next time Fedora refused it. The inner try below covers the resource loop;
        // this one covers everything else, including the Archival Group fetch and the commit.
        try
        {
            logger.LogInformation("(TX) Fedora transaction begun: {TransactionLocation}", transaction.Location);
            var validationResult = await fedoraClient.GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot, transaction);
            if (validationResult.Failure)
            {
                return await FailEarly("Failed to retrieve Archival Group for " + archivalGroupPathUnderRoot, validationResult.ErrorCode);
            }

            var archivalGroup = validationResult.Value;
            string? sourceVersion = null;
            if (!importJob.IsUpdate)
            {
                if(archivalGroup != null)
                {
                    return await FailEarly("Archival Group is not null for new Import: " + archivalGroupPathUnderRoot, ErrorCodes.Conflict);
                }

                if (string.IsNullOrWhiteSpace(importJob.ArchivalGroupName))
                {
                    return await FailEarly("Archival Group does not have a name: " + archivalGroupPathUnderRoot, ErrorCodes.BadRequest);
                }

                Result<ArchivalGroup?>? archivalGroupResult = null;

                try
                {
                    archivalGroupResult = await fedoraClient.CreateArchivalGroup(
                        archivalGroupPathUnderRoot,
                        callerIdentity,
                        importJob.ArchivalGroupName,
                        transaction,
                        cancellationToken);
                }
                catch (Exception e)
                {
                    var resultMessage = "Failed to create archival group";
                    logger.LogError(e, resultMessage);
                    if (archivalGroupResult != null)
                    {
                        resultMessage += " - " + archivalGroupResult.CodeAndMessage();
                    }
                    return await FailEarly("Failed to create archival group: " + archivalGroupPathUnderRoot, resultMessage);
                }

                if (archivalGroupResult.Failure || archivalGroupResult.Value is null)
                {
                    return await FailEarly(
                        $"Failed to create archival group: {archivalGroupPathUnderRoot}, message: {archivalGroupResult.CodeAndMessage()}");
                }
            }
            else
            {
                if(archivalGroup == null)
                {
                    return await FailEarly("Archival Group was null for update: " + archivalGroupPathUnderRoot);
                }
                sourceVersion = archivalGroup.Version!.OcflVersion;
                logger.LogInformation("Archival Group version: {SourceVersion}", sourceVersion);
            }
        
            importJobResult.SourceVersion = sourceVersion;

            logger.LogInformation("Saving running ImportJobResult before processing binaries and containers");
            var savedRunning = await importJobResultStore.SaveRunningImportJobResult(
                request.JobIdentifier, importJobResult, cancellationToken);
            if (savedRunning.Failure || !savedRunning.Value)
            {
                // The row is no longer Active: this job's heartbeat went stale long enough that the
                // reap sweep already recorded it as abandoned (completedWithErrors) before this job
                // actually reached Fedora - a long GC pause or debugger break stalled it between
                // dequeue and here. That record must stand; rolling back now (nothing has been
                // written to Fedora yet) and failing early keeps this call from contradicting it by
                // writing to Fedora, or from flipping Active back to true and masking the reap.
                return await FailEarly(
                    "Import job was already reaped as abandoned (stale heartbeat) before it reached Fedora",
                    ErrorCodes.Conflict);
            }


            logger.LogInformation("(TX) Now looping through import job tasks");
            try
            {
                logger.LogInformation("{Count} containers to add", importJob.ContainersToAdd.Count);
                foreach (var container in importJob.ContainersToAdd.OrderBy(cd => cd.Id!.ToString()))
                {
                    if (await FailIfTransactionLost() is { } lostOnContainersToAdd)
                    {
                        return lostOnContainersToAdd;
                    }
                    logger.LogInformation("(TX) Creating container {Id}", container.Id);
                    var fedoraContainerResult = await fedoraClient.CreateContainerWithinArchivalGroup(
                        container.Id.GetPathUnderRoot()!,
                        callerIdentity,
                        container.Name, transaction, cancellationToken: cancellationToken);
                    if (fedoraContainerResult.Success)
                    {
                        logger.LogInformation("Container created at {Location}", fedoraContainerResult.Value!.Id);
                        importJobResult.ContainersAdded.Add(fedoraContainerResult.Value);
                    }
                    else
                    {
                        return await FailEarly(fedoraContainerResult.CodeAndMessage());
                    }
                }

                // what about deletions of containers? conflict?

                // create files
                logger.LogInformation("{Count} binaries to add", importJob.BinariesToAdd.Count);
                foreach (var binary in importJob.BinariesToAdd)
                {
                    if (await FailIfTransactionLost() is { } lostOnBinariesToAdd)
                    {
                        return lostOnBinariesToAdd;
                    }
                    logger.LogInformation("(TX) Adding binary {Id}, size: {Size}", binary.Id, StringUtils.FormatFileSize(binary.Size));
                    var fedoraPutBinaryResult = await fedoraClient.PutBinary(
                        binary,
                        callerIdentity,
                        transaction, cancellationToken);
                    if (fedoraPutBinaryResult.Success)
                    {
                        logger.LogInformation("Binary created at {Location}", fedoraPutBinaryResult.Value!.Id);
                        importJobResult.BinariesAdded.Add(fedoraPutBinaryResult.Value);
                    }
                    else
                    {
                        return await FailEarly(fedoraPutBinaryResult.CodeAndMessage());
                    }
                }

                // patch files
                // This is EXACTLY the same as Add / PUT.
                // We will need to accomodate some RDF updates - but nothing that can't be carried on BinaryFile
                // nothing _arbitrary_
                logger.LogInformation("{Count} binaries to patch", importJob.BinariesToPatch.Count);
                foreach (var binary in importJob.BinariesToPatch)
                {
                    if (await FailIfTransactionLost() is { } lostOnBinariesToPatch)
                    {
                        return lostOnBinariesToPatch;
                    }
                    logger.LogInformation("(TX) Patching file {Id}, size: {Size}", binary.Id, StringUtils.FormatFileSize(binary.Size));
                    var fedoraPatchBinaryResult = await fedoraClient.PutBinary(
                        binary,
                        callerIdentity,
                        transaction,
                        cancellationToken);
                    if (fedoraPatchBinaryResult.Success)
                    {
                        logger.LogInformation("Binary patched at {Location}", fedoraPatchBinaryResult.Value!.Id);
                        importJobResult.BinariesPatched.Add(fedoraPatchBinaryResult.Value);
                    }
                    else
                    {
                        return await FailEarly(fedoraPatchBinaryResult.CodeAndMessage());
                    }
                }

                // delete files
                logger.LogInformation("{Count} binaries to delete", importJob.BinariesToDelete.Count);
                foreach (var binary in importJob.BinariesToDelete)
                {
                    if (await FailIfTransactionLost() is { } lostOnBinariesToDelete)
                    {
                        return lostOnBinariesToDelete;
                    }
                    logger.LogInformation("(TX) Deleting file {Id}", binary.Id);
                    var fedoraDeleteResult = await fedoraClient.Delete(
                        binary,
                        callerIdentity,
                        transaction,
                        cancellationToken);
                    if (fedoraDeleteResult.Success)
                    {
                        logger.LogInformation("Binary deleted at {Location}", fedoraDeleteResult.Value!.Id);
                        importJobResult.BinariesDeleted.Add((fedoraDeleteResult.Value as Binary)!);
                    }
                    else
                    {
                        return await FailEarly(fedoraDeleteResult.CodeAndMessage());
                    }
                }


                // delete containers
                // Should we verify that the container is empty first?
                // Do we want to allow deletion of non-empty containers? It wouldn't come from a diff importJob
                // but might come from other importJob use.
                logger.LogInformation("{Count} containers to delete", importJob.ContainersToDelete.Count);
                foreach (var container in importJob.ContainersToDelete.OrderByDescending(c => c.Id!.ToString()))
                {
                    if (await FailIfTransactionLost() is { } lostOnContainersToDelete)
                    {
                        return lostOnContainersToDelete;
                    }
                    logger.LogInformation("(TX) Deleting container {Id}", container.Id);
                    var fedoraDeleteResult = await fedoraClient.Delete(
                        container,
                        callerIdentity,
                        transaction,
                        cancellationToken);
                    if (fedoraDeleteResult.Success)
                    {
                        logger.LogInformation("Container deleted at {Location}", fedoraDeleteResult.Value!.Id);
                        importJobResult.ContainersDeleted.Add((fedoraDeleteResult.Value as Container)!);
                    }
                    else
                    {
                        return await FailEarly(fedoraDeleteResult.CodeAndMessage());
                    }
                }
                if (importJob.IsUpdate)
                {
                    var result = await fedoraClient.UpdateContainerMetadata(
                        archivalGroupPathUnderRoot,
                        importJob.ArchivalGroupName,
                        callerIdentity,
                        transaction,
                        cancellationToken);
                    if (result.Failure)
                    {
                        return await FailEarly("Unable to update ArchivalGroup metadata: " + result.ErrorMessage);
                    }
                }
            }
            catch(Exception ex)
            {
                // FailEarly disposes the keep-alive timer, attempts the rollback (guarded, so a rollback
                // that cannot reach Fedora neither escapes nor replaces this exception as the recorded
                // cause) and records ex.Message - the same as every other failing exit from this method.
                logger.LogError(ex, "(TX) Caught error in importJob, rolling back transaction");
                return await FailEarly(ex.Message);
            }

            if (await FailIfTransactionLost() is { } lostBeforeCommit)
            {
                return lostBeforeCommit;
            }

            logger.LogInformation("(TX) Committing Fedora transaction {TransactionLocation}", transaction.Location);
            var startCommitTime = DateTime.UtcNow;
            try
            {
                // The commit PUT can run long - a large commit's connection can stay silent for
                // many minutes (22, in a 2,797-binary/38.7 GB test). The response is an empty 204;
                // Fedora is silent because it's still writing OCFL, not because it's streaming a
                // response slowly (issue #350). The timer is stretched to 30 minutes here so the
                // monitor's post-commit poll - a safety net for the rare case TCP keepalive doesn't
                // cover, not the normal path - stays rare rather than piling up every minute against
                // a commit that is simply still running.
                const int halfAnHour = 30 * 60 * 1000;
                timer.Change(halfAnHour, halfAnHour);
                await transactionMonitor.CommitTransaction();
                // Stops future ticks. A callback already parked at one of its awaits keeps running
                // and can resume after this - the transaction monitor's own disposed-guard is what
                // stops that from crashing the process (see FedoraTransactionMonitor).
                await timer.DisposeAsync();
            }
            catch (Exception e)
            {
                await timer.DisposeAsync(); // stops future ticks; see the comment on the success path above.
                // A connection error during the commit PUT itself - the ALB's ~4,000s idle timeout
                // or a dropped NAT mapping - looks identical to a hang from here, but Fedora may
                // already have committed: the client gets an HttpRequestException or IOException,
                // not confirmation either way (issue #383 review point 2 - this is exactly the
                // failure the safety net exists for). Verify before concluding failure, the same as
                // the monitor-cancelled path below - but only once the transaction itself has
                // actually finished (issue #383's second review, point 2): unlike the
                // monitor-cancelled path, a connection error gives no guarantee Fedora has stopped
                // writing OCFL, and checking the Archival Group's version too early can read it
                // before the commit lands and wrongly call it "confirmed not committed".
                var errorTime = DateTime.UtcNow - startCommitTime;
                var finishedBeforeVerifying = await WaitForTransactionToFinish();
                var outcome = finishedBeforeVerifying
                    ? await VerifyCommitOutcome(sourceVersion)
                    : CommitVerification.CouldNotConfirm;
                if (outcome == CommitVerification.Confirmed)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "(TX) Commit outcome confirmed despite a connection error during the commit PUT for transaction {TransactionLocation}: {Error}",
                            transaction.Location, e.Message);
                    }
                    importJobResult.DateFinished = DateTime.UtcNow;
                    importJobResult.Status = ImportJobStates.Completed;
                    return Result.OkNotNull(importJobResult);
                }
                var confirmation = outcome == CommitVerification.ConfirmedNotCommitted
                    ? "confirmed not committed"
                    : "could not be confirmed";
                var message = $"(TX) Unable to commit Fedora transaction: duration {errorTime.TotalSeconds} seconds: {e.Message} (commit outcome {confirmation})";
                logger.LogError(e, message);
                return await FailEarly(message, rollback: false);
            }
            importJobResult.DateFinished = DateTime.UtcNow;
            var commitDuration = importJobResult.DateFinished - startCommitTime;
            logger.LogInformation("(TX) Fedora commit transaction took {Duration} seconds", commitDuration.Value.TotalSeconds);

            if (transaction.Cancelled)
            {
                // The monitor's post-commit poll found the transaction gone and cancelled the PUT -
                // previously treated as if the commit had succeeded (issue #350). The PUT's actual
                // outcome is genuinely unknown from here: Fedora may have committed successfully
                // before the connection's NAT mapping expired (the ALB still delivers the response
                // in that case - see the issue's evidence), or the transaction may really have been
                // rolled back, by Fedora's own session expiry during a stall. Confirm which with a
                // cheap version check rather than a full read of the group (issue #244).
                var outcome = await VerifyCommitOutcome(sourceVersion);
                if (outcome != CommitVerification.Confirmed)
                {
                    var confirmation = outcome == CommitVerification.ConfirmedNotCommitted
                        ? "confirmed not committed"
                        : "could not be confirmed";
                    return await FailEarly(
                        $"(TX) Fedora transaction commit outcome {confirmation} after the monitor cancelled the commit PUT",
                        rollback: false);
                }
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "(TX) Commit outcome confirmed despite the cancelled PUT for transaction {TransactionLocation}", transaction.Location);
                }
            }
            else if ((int)transaction.CommitStatusCode is < 200 or >= 300)
            {
                if (IsTransientGatewayStatus(transaction.CommitStatusCode))
                {
                    // The ALB's own ~4,000s idle timeout on a long commit arrives as an HTTP 504
                    // response, not an exception - a dropped target connection becomes a 502 -
                    // so CommitTransaction returns normally with this recorded, rather than
                    // throwing (issue #383's second review, point 1). A commit running that long
                    // behaves exactly like the connection-error path above: the response can't be
                    // trusted, and Fedora may already have committed or still be writing OCFL, so
                    // wait for the transaction to finish before checking the version.
                    var finishedBeforeVerifying = await WaitForTransactionToFinish();
                    var gatewayOutcome = finishedBeforeVerifying
                        ? await VerifyCommitOutcome(sourceVersion)
                        : CommitVerification.CouldNotConfirm;
                    if (gatewayOutcome == CommitVerification.Confirmed)
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                        {
                            logger.LogInformation(
                                "(TX) Commit outcome confirmed despite an HTTP {StatusCode} commit response for transaction {TransactionLocation}",
                                (int)transaction.CommitStatusCode, transaction.Location);
                        }
                        importJobResult.DateFinished = DateTime.UtcNow;
                        importJobResult.Status = ImportJobStates.Completed;
                        return Result.OkNotNull(importJobResult);
                    }
                    var gatewayConfirmation = gatewayOutcome == CommitVerification.ConfirmedNotCommitted
                        ? "confirmed not committed"
                        : "could not be confirmed";
                    return await FailEarly(
                        $"(TX) Fedora transaction commit returned HTTP status {(int)transaction.CommitStatusCode} " +
                        $"({transaction.CommitStatusCode}) (commit outcome {gatewayConfirmation})",
                        rollback: false);
                }

                // CommitTransaction records the status but never throws for a non-2xx response
                // (issue #350), so a committed-but-rejected transaction (409, 410, and other
                // statuses that come from Fedora itself) would otherwise be reported as success.
                // Unlike the ALB's own 502/503/504 above, these are real answers from Fedora: no
                // verification needed, the rejection is already certain.
                return await FailEarly(
                    $"(TX) Fedora transaction commit returned HTTP status {(int)transaction.CommitStatusCode} ({transaction.CommitStatusCode})",
                    rollback: false);
            }

            importJobResult.Status = ImportJobStates.Completed;
            return Result.OkNotNull(importJobResult);
        }
        catch (Exception e)
        {
            logger.LogError(e, "(TX) Unhandled error while executing import job {JobIdentifier}; failing it and rolling back",
                request.JobIdentifier);
            return await FailEarly("Import job failed: " + e.Message);
        }

        
        async Task<Result<ImportJobResult>> FailEarly(string? errorMessage, string? errorCode = ErrorCodes.UnknownError, bool rollback = true)
        {
            await timer.DisposeAsync();
            logger.LogError("(TX) Failing Import Job Early: {ErrorCode} - {ErrorMessage}", errorCode, errorMessage);
            if (rollback)
            {
                try
                {
                    await fedoraClient.RollbackTransaction(transaction);
                }
                catch (Exception rollbackException)
                {
                    // The job is already failed; a rollback that cannot reach Fedora must not turn a
                    // failed job into an escaped exception. Fedora expires an uncommitted transaction itself.
                    logger.LogError(rollbackException, "(TX) Rollback of {TransactionLocation} failed", transaction.Location);
                }
            }
            importJobResult.Errors = [new Error { Message = errorMessage ?? "" }];
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }

        /// <summary>
        /// Null to carry on; a result to return immediately when the pre-commit keep-alive has
        /// found the transaction gone (issue #350). Checked between items in every loop above and
        /// once more immediately before the commit, so a transaction lost mid-stream - through a
        /// stall long enough to pass Fedora's 3-minute session timeout - is noticed before writing
        /// into it further or reaching a commit whose outcome would then be undefined, rather than
        /// only once the commit itself fails or does something unexpected.
        /// </summary>
        async Task<Result<ImportJobResult>?> FailIfTransactionLost()
        {
            if (!transaction.TransactionLost)
            {
                return null;
            }
            return await FailEarly(
                $"(TX) Fedora transaction {transaction.Location} was lost before the commit could be attempted " +
                "(the pre-commit keep-alive found it gone)",
                rollback: false);
        }

        /// <summary>
        /// Polls the transaction itself (not the Archival Group) until Fedora stops reporting it as
        /// mid-commit - a GET answers 2xx or 409 while the commit is still running (FedoraTransactionMonitor
        /// uses the same two codes to decide the same thing), anything else once it has finished one
        /// way or another. Needed before <see cref="VerifyCommitOutcome"/> runs on the connection-error
        /// and ALB-gateway paths, where the commit PUT's own response can't be trusted and Fedora may
        /// still be writing OCFL: checking the Archival Group's version too early can read it before
        /// the commit lands and wrongly report "confirmed not committed" (issue #383's second review,
        /// point 2). Not needed on the monitor-cancelled path below, which by definition only cancels
        /// once a GET on the transaction has already returned a non-2xx, non-409 status - the
        /// transaction has already finished by the time that happens.
        ///
        /// CancellationToken.None for the wait between polls, not the job's own token (issue #383's
        /// second review, first nit): by this point work may already be committed to Fedora, so a
        /// caller shutdown must not cut this short and let the OperationCanceledException escape to
        /// the outer catch, which would roll back - via FailEarly's default rollback:true - a
        /// transaction Fedora may have already acted on. The same applies to the backoff delay inside
        /// VerifyCommitOutcome below.
        /// </summary>
        async Task<bool> WaitForTransactionToFinish()
        {
            var waited = TimeSpan.Zero;
            while (waited < TransactionFinishMaxWait)
            {
                HttpStatusCode status;
                try
                {
                    status = await fedoraClient.GetTransactionHttpStatus(transaction);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "(TX) Polling transaction {TransactionLocation} to see if it has finished threw; proceeding to verify the Archival Group directly",
                        transaction.Location);
                    return true;
                }

                var statusCode = (int)status;
                if (status != HttpStatusCode.Conflict && statusCode is < 200 or > 299)
                {
                    return true;
                }

                await delay(TransactionFinishPollInterval, CancellationToken.None);
                waited += TransactionFinishPollInterval;
            }

            logger.LogWarning(
                "(TX) Transaction {TransactionLocation} still reported as committing after {MaxWait}; giving up without verifying the Archival Group",
                transaction.Location, TransactionFinishMaxWait);
            return false;
        }

        /// <summary>
        /// Whether the Archival Group's own state confirms the commit actually happened, for the
        /// two cases where the commit PUT's own response can't be trusted: the monitor cancelled it
        /// after finding the transaction gone, or the PUT itself threw a connection error (issue
        /// #383 review points 1 and 2). Polls with backoff rather than checking once - in both
        /// cases Fedora may still be finishing the commit when this runs, and the October test's
        /// 22-minute commit is not far under the monitor's own 30-minute cancel threshold. A cheap
        /// version check each time (issue #244: a full read of a wide group can take 10-30 minutes),
        /// never a cache of anything read before the commit - this has to be the real post-commit
        /// state. A new group existing at all, with a version, is success; an update needs its
        /// version to have actually moved past where it started - unchanged means the transaction
        /// never committed, the same as the group being missing entirely would.
        ///
        /// Any exception from the read itself - including a brand-new group that doesn't exist in
        /// S3 yet, which GetArchivalGroupVersion throws for rather than returning NotFound (issue
        /// #383 review point 3) - is caught here and treated as an unconfirmed attempt, not a
        /// reason to let the whole import job fail with a misleading rollback of an already-acted-on
        /// transaction. The one exception: for a create (never an update, whose Archival Group
        /// already existed before the job started), that same "doesn't exist in S3" throw is
        /// genuine evidence the create never committed, not just an unreadable attempt - but only
        /// because every caller of VerifyCommitOutcome has already waited for the transaction
        /// itself to finish (WaitForTransactionToFinish above, or the monitor-cancelled path's own
        /// equivalent guarantee), so there's nothing left for a retry to wait out (issue #383's
        /// second review, second nit).
        /// </summary>
        async Task<CommitVerification> VerifyCommitOutcome(string? sourceVersion)
        {
            var everReadSuccessfully = false;
            for (var attempt = 1; attempt <= CommitVerificationMaxAttempts; attempt++)
            {
                try
                {
                    var versionResult = await fedoraClient.GetArchivalGroupVersion(archivalGroupPathUnderRoot);
                    if (versionResult is { Success: true, Value: not null })
                    {
                        everReadSuccessfully = true;
                        var newVersion = versionResult.Value.OcflVersion;
                        var committed = importJob.IsUpdate
                            ? newVersion.HasText() && newVersion != sourceVersion
                            : newVersion.HasText();
                        if (committed)
                        {
                            return CommitVerification.Confirmed;
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath}: {CodeAndMessage}",
                            attempt, CommitVerificationMaxAttempts, archivalGroupPathUnderRoot, versionResult.CodeAndMessage());
                    }
                }
                catch (AmazonS3Exception ex) when (!importJob.IsUpdate && ex.StatusCode == HttpStatusCode.NotFound)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(ex,
                            "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath}: the Archival Group still doesn't exist in S3, and the transaction has already finished, so the create never committed",
                            attempt, CommitVerificationMaxAttempts, archivalGroupPathUnderRoot);
                    }
                    return CommitVerification.ConfirmedNotCommitted;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "(TX) Commit verification attempt {Attempt}/{MaxAttempts} for {ArchivalGroupPath} threw",
                        attempt, CommitVerificationMaxAttempts, archivalGroupPathUnderRoot);
                }

                if (attempt < CommitVerificationMaxAttempts)
                {
                    // CancellationToken.None, not the job's own token - see WaitForTransactionToFinish's
                    // remarks above (issue #383's second review, first nit).
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), CancellationToken.None);
                }
            }

            return everReadSuccessfully ? CommitVerification.ConfirmedNotCommitted : CommitVerification.CouldNotConfirm;
        }
    }

    /// <summary>
    /// Everything about a job that can be judged without touching Fedora, judged before anything is
    /// touched. Called by the queue handler, so a caller gets a 400 rather than a queued job that
    /// completes with errors, and again here, because a job can also arrive from the queue directly.
    /// In particular every repository path in the job - the Archival Group and each resource id - must
    /// be a path under the root (<see cref="SafeRepositoryPath"/>): <see cref="Converters.GetFedoraUri"/>
    /// throws for one that is not, and here that would happen with a Fedora transaction open and
    /// outside any catch, which would take the whole import host down with it. Also refuses a job
    /// that asks for a rename, which this executor never performs (issue #260) - the Preservation
    /// API refuses one first for a job posted there, but a job reaching this API directly (POST
    /// /import) or arriving from the queue is judged here too.
    /// </summary>
    internal static Result PreProcessValidateImportJob(ImportJob importJob)
    {
        if (importJob.ArchivalGroup == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Import job has no Archival Group");
        }
        if (!SafeRepositoryPath.IsRepositoryPath(importJob.ArchivalGroup.GetPathUnderRoot(), out var agReason))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"Archival Group {importJob.ArchivalGroup} is not a path under the repository root: {agReason}");
        }
        if (importJob.RenameRefusalMessage() is { } renameMessage)
        {
            return Result.Fail(ErrorCodes.BadRequest, renameMessage);
        }

        var binaryIds = importJob.BinariesToAdd
            .Union(importJob.BinariesToPatch)
            .Union(importJob.BinariesToRename)
            .Union(importJob.BinariesToDelete)
            .Select(binary => binary.Id);
        var containerIds = importJob.ContainersToAdd
            .Union(importJob.ContainersToRename)
            .Union(importJob.ContainersToDelete)
            .Select(container => container.Id);

        return binaryIds.Select(id => ValidateResourceId(id, "Binary", importJob.ArchivalGroup))
            .Concat(containerIds.Select(id => ValidateResourceId(id, "Container", importJob.ArchivalGroup)))
            .FirstOrDefault(result => result.Failure) ?? Result.Ok();
    }

    /// <summary>
    /// The checks every binary and container id in a job must pass before anything is touched: it
    /// exists, carries no fragment, is a path under the repository root
    /// (<see cref="SafeRepositoryPath"/>), and lies strictly below the job's Archival Group. The
    /// last is the Storage API's own copy of the check the Preservation API makes on a posted job -
    /// a job that declares the right group but names a resource in another would otherwise write
    /// there - compared by repository path so the host does not matter, and strictly below so the
    /// group itself cannot be named as a resource.
    /// </summary>
    private static Result ValidateResourceId(Uri? id, string kind, Uri archivalGroup)
    {
        if (id == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, $"{kind} ID is null");
        }
        if (id.LocalPath.Contains('#'))
        {
            return Result.Fail(ErrorCodes.BadRequest, $"{kind} ID contains a fragment identifier (#): {id}");
        }
        var pathUnderRoot = id.GetPathUnderRoot();
        if (!SafeRepositoryPath.IsRepositoryPath(pathUnderRoot, out var reason))
        {
            return Result.Fail(ErrorCodes.BadRequest, $"{kind} ID {id} is not a path under the repository root: {reason}");
        }
        var groupPath = archivalGroup.GetPathUnderRoot()!.TrimEnd('/') + '/';
        if (pathUnderRoot?.StartsWith(groupPath, StringComparison.Ordinal) != true)
        {
            return Result.Fail(ErrorCodes.BadRequest, $"{kind} ID {id} is not within the job's Archival Group {archivalGroup}");
        }
        return Result.Ok();
    }
}