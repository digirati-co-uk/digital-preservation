using System.Diagnostics;
using System.Net;
using DigitalPreservation.Utils;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public sealed class FedoraTransactionMonitor(
    ILogger logger,
    IFedoraClient fedoraClient,
    Transaction tx,
    Stopwatch stopwatch) : IDisposable
{
    private readonly CancellationTokenSource cancellationTokenSource = new();

    // Written by Dispose(), read by the timer callback, which can still be running (parked at one
    // of its awaits) after Dispose() has returned - see MaintainTransactionState's cancel path.
    private volatile bool disposed;

    public async Task CommitTransaction()
    {
        tx.CommitStarted = true;
        // Only ExecuteImportJob reads this token, and it always does so before disposing this
        // monitor, so no disposed-guard is needed here - unlike the timer callback below, which can
        // still be running after Dispose() is called.
        var token = cancellationTokenSource.Token;
        token.ThrowIfCancellationRequested();
        try
        {
            await fedoraClient.CommitTransaction(tx, token);
            tx.CommitReturned = true;
        }
        catch (OperationCanceledException)
        {
            // Expected control flow: this is the deliberate cancel-the-HTTP-request path,
            // so don't attach the exception - a stack trace here is noise, not signal.
            tx.Cancelled = true;
            logger.LogWarning("(TX) fedoraClient.CommitTransaction for {Transaction} was cancelled (HTTP Request was cancelled)", tx.Location.GetSlug());
        }
        // throw any other exception
    }

    public async void MaintainTransactionState(object? state)
    {
        if (state != tx)
        {
            logger.LogError("(TX) (M) State passed to timer is not the transaction.");
            return;
        }

        var transactionId = tx.Location.GetSlug();

        // Nothing below may escape this method: it's a TimerCallback, so it has to stay async void,
        // and an exception escaping an async void terminates the process.
        try
        {
            logger.LogInformation("(TX) (M) Monitoring transaction {TransactionId}", transactionId);
            if (tx.CommitReturned)
            {
                logger.LogInformation("(TX) (M) Transaction {TransactionId} request has already returned, will not maintain it.", transactionId);
                return;
            }

            if (tx.CommitStarted)
            {
                logger.LogInformation("(TX) (M) Transaction {TransactionId} is currently being committed", transactionId);
                bool cancel = tx.CancelRequested;
                // This branch is a documented safety net, not the fix for a real Fedora hang
                // (issue #350): a commit silent for a very long time is almost always the NAT
                // gateway's 350s idle timeout on the connection between us and Fedora, which TCP
                // keepalive now handles - a 38.7 GB, 2,797-binary commit that was silent for 22
                // minutes still returned 204 once keepalive was in place. This poll only matters
                // for the gap keepalive doesn't cover: the ALB's own ~4,000s idle-timeout ceiling,
                // which keepalive probes don't reset, and any future network path change. The timer
                // is stretched to 30 minutes once the commit starts specifically so this poll is rare.
                //
                // Only a status GET runs here - no keep-alive POST (issue #383 review point 1): a
                // keep-alive POSTed against a transaction that is mid-commit is itself what Fedora
                // answers with 409, so POSTing one here was cancelling commits that were simply
                // still running, not actually lost.
                var currentStatus = await fedoraClient.GetTransactionHttpStatus(tx);
                var currentStatusCode = (int)currentStatus;
                logger.LogInformation("(TX) (M) Transaction {TransactionId} has HTTP Status {StatusCode}.", transactionId, currentStatusCode);
                if (currentStatus == HttpStatusCode.Conflict)
                {
                    // 409 on a GET of a transaction that is mid-commit means Fedora is still
                    // committing it, not that the transaction is gone - this is "still running",
                    // not grounds to cancel (issue #383 review point 1).
                    logger.LogInformation(
                        "(TX) (M) Transaction {TransactionId} returned 409 (Conflict) while committing - still committing, not cancelling.",
                        transactionId);
                }
                else if (currentStatusCode < 200 || currentStatusCode > 299)
                {
                    logger.LogInformation("(TX) (M) Transaction {TransactionId} has non-2xx status ({StatusCode}), will cancel the commit if not already requested to cancel.",
                        transactionId, currentStatusCode);
                    cancel = true;
                }

                if (cancel)
                {
                    if (tx.CancelRequested)
                    {
                        logger.LogWarning("(TX) (M) Cancel already requested for transaction {TransactionId}, will continue", transactionId);
                    }
                    else
                    {
                        logger.LogWarning("(TX) (M) Cancelling transaction {TransactionId}", transactionId);
                        tx.CancelRequested = true;
                        // disposed is checked first to avoid the exception in the common case, but the
                        // check and the call can still race with Dispose() - the catch handles that.
                        if (disposed)
                        {
                            logger.LogDebug("(TX) (M) Transaction monitor for {TransactionId} already disposed; not cancelling.", transactionId);
                        }
                        else
                        {
                            try
                            {
                                await cancellationTokenSource.CancelAsync();
                            }
                            catch (ObjectDisposedException ex)
                            {
                                logger.LogDebug(ex, "(TX) (M) Transaction monitor for {TransactionId} was disposed while cancelling; nothing left to cancel.", transactionId);
                            }
                        }
                    }
                }
            }
            else
            {
                logger.LogInformation("(TX) (M) (commit not started) Keeping transaction {TransactionId} alive after {ElapsedMilliseconds} ms",
                    transactionId, stopwatch.ElapsedMilliseconds);
                await fedoraClient.KeepTransactionAlive(tx);
                var statusCode = tx.StatusCode;
                if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Fedora has expired or otherwise lost this transaction (issue #350) - most
                    // likely during a stall long enough to pass its 3-minute session timeout. Record
                    // it so the import loop can stop feeding a transaction that is already gone,
                    // rather than reaching a commit whose outcome would then be undefined. Scoped to
                    // 404/410 specifically (issue #383 review point 6), as the issue originally
                    // asked: a transient 502/503/504 from the ALB on a single keep-alive is not
                    // evidence the transaction itself is gone, and must not end a multi-hour import
                    // over one blip.
                    logger.LogWarning(
                        "(TX) (M) KeepTransactionAlive for transaction {TransactionId} returned {StatusCode}; the transaction appears to be lost.",
                        transactionId, (int)statusCode);
                    tx.TransactionLost = true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "(TX) (M) Monitoring transaction {TransactionId} failed", transactionId);
        }
    }

    public void Dispose()
    {
        disposed = true;
        cancellationTokenSource.Dispose();
    }
}
