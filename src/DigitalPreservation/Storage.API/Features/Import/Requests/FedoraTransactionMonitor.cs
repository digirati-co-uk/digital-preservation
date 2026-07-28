using System.Diagnostics;
using DigitalPreservation.Utils;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public class FedoraTransactionMonitor(
    ILogger logger,
    IFedoraClient fedoraClient,
    Transaction tx,
    Stopwatch stopwatch)
{
    private readonly CancellationTokenSource cancellationTokenSource = new();

    public async Task CommitTransaction()
    {
        tx.CommitStarted = true;
        var token = cancellationTokenSource.Token;
        token.ThrowIfCancellationRequested();
        try
        {
            await fedoraClient.CommitTransaction(tx, token);
            tx.CommitReturned = true;
        }
        catch (OperationCanceledException oce)
        {
            tx.Cancelled = true;
            logger.LogWarning(oce, "(TX) fedoraClient.CommitTransaction for {Transaction} was cancelled (HTTP Request was cancelled)", tx.Location.GetSlug());
        }
        // throw any other exception
    }

    public async void MaintainTransactionState(object? state)
    {
        if (state != tx)
        {
            throw new NotSupportedException("State passed to timer is not the transaction.");
        }

        var transactionId = tx.Location.GetSlug();
        
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
            // While Fedora is in its emit events stage, this WILL NOT RETURN until Fedora has finished
            // So we get a massive pileup of these calls because the timer is still ticking every minute,
            // re-entering this method.
            
            // Try it again with stopping the timer completely once commit called
            // Try it again without the following line, instead just fedoraClient.KeepTransactionAlive(tx) - does that return immediately?
            var currentStatus = await fedoraClient.GetTransactionHttpStatus(tx);
            var currentStatusCode = (int)currentStatus;
            logger.LogInformation("(TX) (M) Transaction {TransactionId} has HTTP Status {StatusCode}.", transactionId, currentStatusCode);
            if (currentStatusCode < 200 || currentStatusCode > 299)
            {
                // don't even try to PUT a keep-alive
                logger.LogInformation("(TX) (M) Transaction {TransactionId} has non-2xx status ({StatusCode}), will cancel the commit if not already requested to cancel.",
                    transactionId, currentStatusCode);
                cancel = true;
            }

            if (!cancel)
            {
                logger.LogInformation("(TX) (M) Keeping commit of transaction {TransactionId} alive after {ElapsedMilliseconds} ms",
                    transactionId, stopwatch.ElapsedMilliseconds);
                try
                {
                    await fedoraClient.KeepTransactionAlive(tx);
                    currentStatusCode = (int)tx.StatusCode;
                    if (currentStatusCode < 200 || currentStatusCode > 299)
                    {
                        logger.LogWarning("(TX) (M) KeepTransactionAlive for transaction {TransactionId} returned {StatusCode}, will cancel the commit.",
                            transactionId, currentStatusCode);
                        cancel = true;
                    }
                    else
                    {
                        logger.LogInformation("(TX) (M) After keep-alive, transaction {TransactionId} has status {StatusCode}", transactionId, currentStatusCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "(TX) (M) Keeping transaction {TransactionId} alive failed: {StatusCode}, will cancel the commit.", transactionId, (int)tx.StatusCode);
                    cancel = true;
                }
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
                    await cancellationTokenSource.CancelAsync();
                }
            }
        }
        else
        {
            logger.LogInformation("(TX) (M) (commit not started) Keeping transaction {TransactionId} alive after {ElapsedMilliseconds} ms",
                transactionId, stopwatch.ElapsedMilliseconds);
            await fedoraClient.KeepTransactionAlive(tx);
        }
    }
}