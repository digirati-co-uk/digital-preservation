using System.Diagnostics;
using System.Security.Cryptography;
using DigitalPreservation.Utils;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public class FedoraTransactionMonitor(
    ILogger<ExecuteImportJobHandler> logger,
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
        catch (OperationCanceledException)
        {
            tx.Cancelled = true;
            logger.LogWarning("(TX) fedoraClient.CommitTransaction for {transaction} was cancelled (HTTP Request was cancelled)", tx.Location.GetSlug());
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
        
        logger.LogInformation("(TX) (M) Monitoring transaction {transactionId}", transactionId);
        if (tx.CommitReturned)
        {
            logger.LogInformation("(TX) (M) Transaction {transactionId} request has already returned, will not maintain it.", transactionId);
            return;
        }
        
        if (tx.CommitStarted)
        {
            logger.LogInformation("(TX) (M) Transaction {transactionId} is currently being committed", transactionId);
            bool cancel = tx.CancelRequested;
            var currentStatus = await fedoraClient.GetTransactionHttpStatus(tx);
            var currentStatusCode = (int)currentStatus;
            logger.LogInformation("(TX) (M) Transaction {transactionId} has HTTP Status {statusCode}.", transactionId, currentStatusCode);
            if (currentStatusCode < 200 || currentStatusCode > 299)
            {
                // don't even try to PUT a keep-alive
                logger.LogInformation("(TX) (M) Transaction {transactionId} has non-2xx status ({statusCode}), will cancel the commit if not already requested to cancel.", 
                    transactionId, currentStatusCode);
                cancel = true;
            }

            if (!cancel)
            {
                logger.LogInformation("(TX) (M) Keeping commit of transaction {transactionId} alive after {elapsedMilliseconds} ms", 
                    transactionId, stopwatch.ElapsedMilliseconds);
                try
                {
                    await fedoraClient.KeepTransactionAlive(tx);
                    currentStatusCode = (int)tx.StatusCode;
                    if (currentStatusCode < 200 || currentStatusCode > 299)
                    {
                        logger.LogWarning("(TX) (M) KeepTransactionAlive for transaction {transactionId} returned {statusCode}, will cancel the commit.",
                            transactionId, currentStatusCode);
                        cancel = true;
                    }
                    else
                    {
                        logger.LogInformation("(TX) (M) After keep-alive, transaction {transactionId} has status {statusCode}", transactionId, currentStatusCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "(TX) (M) Keeping transaction {transactionId} alive failed: {statusCode}, will cancel the commit.", transactionId, (int)tx.StatusCode);
                    cancel = true;
                }
            }

            if (cancel)
            {
                if (tx.CancelRequested)
                {
                    logger.LogWarning("(TX) (M) Cancel already requested for transaction {transactionId}, will continue", transactionId);
                }
                else
                {
                    logger.LogWarning("(TX) (M) Cancelling transaction {transactionId}", transactionId);
                    tx.CancelRequested = true;
                    await cancellationTokenSource.CancelAsync();
                }
            }
        }
        else
        {
            logger.LogInformation("(TX) (M) (commit not started) Keeping transaction {transactionId} alive after {elapsedMilliseconds} ms", 
                transactionId, stopwatch.ElapsedMilliseconds);
            await fedoraClient.KeepTransactionAlive(tx);
        }
    }
}