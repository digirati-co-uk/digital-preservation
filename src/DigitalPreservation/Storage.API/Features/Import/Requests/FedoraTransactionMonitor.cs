using System.Diagnostics;
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
        }
        catch (OperationCanceledException oce)
        {
            tx.Cancelled = true;
            logger.LogWarning("Transaction commit for {transaction} was cancelled", tx.Location.GetSlug());
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
        
        logger.LogInformation("Monitoring transaction {transactionId}", transactionId);
        
        if (tx.CommitStarted)
        {
            logger.LogInformation("Transaction {transactionId} is currently being committed", transactionId);
            bool cancel = false;
            var currentStatus = await fedoraClient.GetTransactionHttpStatus(tx);
            if ((int)currentStatus < 200 || (int)currentStatus > 299)
            {
                // don't even try to PUT a keep-alive
                logger.LogInformation("Transaction {transactionId} has status {statusCode}, will cancel the commit.", 
                    transactionId, currentStatus);
                cancel = true;
            }

            if (!cancel)
            {
                logger.LogInformation("Keeping commit of transaction {transactionId} alive after {elapsedMilliseconds} ms", 
                    transactionId, stopwatch.ElapsedMilliseconds);
                try
                {
                    await fedoraClient.KeepTransactionAlive(tx);
                    if ((int)tx.StatusCode < 200 || (int)tx.StatusCode > 299)
                    {
                        logger.LogWarning("KeepTransactionAlive failed for {transactionId}: {statusCode}, will cancel the commit.",
                            transactionId, tx.StatusCode);
                        cancel = true;
                    }
                    else
                    {
                        logger.LogInformation("Transaction {transactionId} has Http Status {statusCode}", transactionId, tx.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Keeping transaction {transactionId} alive failed: {statusCode}", transactionId, tx.StatusCode);
                    cancel = true;
                }
            }

            if (cancel)
            {
                logger.LogWarning(" Cancelling transaction {transactionId}", transactionId);
                await cancellationTokenSource.CancelAsync();
            }

        }
        else
        {
            logger.LogInformation("Keeping transaction {transactionId} alive after {elapsedMilliseconds} ms", 
                transactionId, stopwatch.ElapsedMilliseconds);
            await fedoraClient.KeepTransactionAlive(tx);
        }
    }
}