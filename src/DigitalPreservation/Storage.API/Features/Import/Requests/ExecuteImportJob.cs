using System.Diagnostics;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
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
    IFedoraClient fedoraClient) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(ExecuteImportJob request, CancellationToken cancellationToken)
    {
        var importJob = request.ImportJob;
        var archivalGroupPathUnderRoot = importJob.ArchivalGroup.GetPathUnderRoot()!;
        logger.LogInformation("Executing Import Job");
        
        var start = DateTime.UtcNow;
        
        var transaction = await fedoraClient.BeginTransaction();
        logger.LogInformation("Fedora transaction begun: " + transaction.Location);
        var validationResult = await fedoraClient.GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot, transaction);
        if (validationResult.Failure)
        {
            await fedoraClient.RollbackTransaction(transaction);
            logger.LogError("Failed to retrieve Archival Group for " + archivalGroupPathUnderRoot);
            return Result.ConvertFailNotNull<ArchivalGroup?, ImportJobResult>(validationResult);
        }

        var archivalGroup = validationResult.Value;
        string? sourceVersion = null;
        if (!importJob.IsUpdate)
        {
            if(archivalGroup != null)
            {
                await fedoraClient.RollbackTransaction(transaction);
                logger.LogError("Archival Group is not null for new Import: " + archivalGroupPathUnderRoot);
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.Conflict, "An Archival Group has recently been created at " + archivalGroupPathUnderRoot);
            }

            if (string.IsNullOrWhiteSpace(importJob.ArchivalGroupName))
            {
                await fedoraClient.RollbackTransaction(transaction);
                logger.LogError("Archival Group does not have a name: " + archivalGroupPathUnderRoot);
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, "No name supplied for this archival group");
            }

            var archivalGroupResult = await fedoraClient.CreateArchivalGroup(
                archivalGroupPathUnderRoot,
                importJob.ArchivalGroupName,
                transaction,
                cancellationToken);

            if (archivalGroupResult.Failure || archivalGroupResult.Value is null)
            {
                await fedoraClient.RollbackTransaction(transaction);
                logger.LogError("Failed to create archival group: " + archivalGroupPathUnderRoot);
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "No archival group was returned from creation");
            }
        }
        else
        {
            if(archivalGroup == null)
            {
                await fedoraClient.RollbackTransaction(transaction);
                logger.LogError("Archival Group was null for update: " + archivalGroupPathUnderRoot);
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.NotFound, "Not an update but no Archival Group at " + archivalGroupPathUnderRoot);
            }
            sourceVersion = archivalGroup.Version!.OcflVersion;
            logger.LogInformation("Archival Group version: " + sourceVersion);
        }
        
        // We need to keep the transaction alive throughout this process
        // will need to time operations and call fedora.KeepTransactionAlive

        var importJobResult = request.InitialImportJobResult;
        importJobResult.Status = ImportJobStates.Running;
        importJobResult.SourceVersion = sourceVersion;
        importJobResult.DateBegun = start;
        importJobResult.LastModified = start;
        
        logger.LogInformation("Saving running ImportJobResult");
        await importJobResultStore.SaveImportJobResult(request.JobIdentifier, importJobResult, true, cancellationToken);

        var timer = new Stopwatch();
        timer.Start();
        
        logger.LogInformation("Now looping through import job tasks");
        try
        {
            logger.LogInformation("{count} containers to add", importJob.ContainersToAdd.Count);
            foreach (var container in importJob.ContainersToAdd.OrderBy(cd => cd.Id!.ToString()))
            {
                logger.LogInformation("Creating container {id}", container.Id);
                var fedoraContainerResult = await fedoraClient.CreateContainerWithinArchivalGroup(container.Id.GetPathUnderRoot()!, container.Name, transaction, cancellationToken: cancellationToken);
                if (fedoraContainerResult.Success)
                {
                    logger.LogInformation("Container created at {location}", fedoraContainerResult.Value!.Id);
                    importJobResult.ContainersAdded.Add(fedoraContainerResult.Value!);
                }
                else
                {
                    return await FailEarly(fedoraContainerResult.CodeAndMessage());
                }
                await KeepTransactionAlive();
            }

            // what about deletions of containers? conflict?

            // create files
            logger.LogInformation("{count} binaries to add", importJob.BinariesToAdd.Count);
            foreach (var binary in importJob.BinariesToAdd)
            {
                logger.LogInformation("Adding binary {id}", binary.Id);
                var fedoraPutBinaryResult = await fedoraClient.PutBinary(binary, transaction, cancellationToken);
                if (fedoraPutBinaryResult.Success)
                {
                    logger.LogInformation("Binary created at {location}", fedoraPutBinaryResult.Value!.Id);
                    importJobResult.BinariesAdded.Add(fedoraPutBinaryResult.Value!);
                }
                else
                {
                    return await FailEarly(fedoraPutBinaryResult.CodeAndMessage());
                }
                await KeepTransactionAlive();
            }

            // patch files
            // This is EXACTLY the same as Add / PUT.
            // We will need to accomodate some RDF updates - but nothing that can't be carried on BinaryFile
            // nothing _arbitrary_
            logger.LogInformation("{count} binaries to patch", importJob.BinariesToPatch.Count);
            foreach (var binary in importJob.BinariesToPatch)
            {
                logger.LogInformation("Patching file {id}", binary.Id);
                var fedoraPatchBinaryResult = await fedoraClient.PutBinary(binary, transaction, cancellationToken);
                if (fedoraPatchBinaryResult.Success)
                {
                    logger.LogInformation("Binary patched at {location}", fedoraPatchBinaryResult.Value!.Id);
                    importJobResult.BinariesPatched.Add(fedoraPatchBinaryResult.Value!);
                }
                else
                {
                    return await FailEarly(fedoraPatchBinaryResult.CodeAndMessage());
                }
                await KeepTransactionAlive();
            }

            // delete files
            logger.LogInformation("{count} binaries to delete", importJob.BinariesToDelete.Count);
            foreach (var binary in importJob.BinariesToDelete)
            {
                logger.LogInformation("Deleting file {id}", binary.Id);
                var fedoraDeleteResult = await fedoraClient.Delete(binary, transaction, cancellationToken);
                if (fedoraDeleteResult.Success)
                {
                    logger.LogInformation("Binary deleted at {location}", fedoraDeleteResult.Value!.Id);
                    importJobResult.BinariesDeleted.Add((fedoraDeleteResult.Value as Binary)!);
                }
                else
                {
                    return await FailEarly(fedoraDeleteResult.CodeAndMessage());
                }
                await KeepTransactionAlive();
            }


            // delete containers
            // Should we verify that the container is empty first?
            // Do we want to allow deletion of non-empty containers? It wouldn't come from a diff importJob
            // but might come from other importJob use.
            logger.LogInformation("{count} containers to delete", importJob.ContainersToDelete.Count);
            foreach (var container in importJob.ContainersToDelete.OrderByDescending(c => c.Id!.ToString()))
            {
                logger.LogInformation("Deleting container {id}", container.Id);
                var fedoraDeleteResult = await fedoraClient.Delete(container, transaction, cancellationToken);
                if (fedoraDeleteResult.Success)
                {
                    logger.LogInformation("Container deleted at {location}", fedoraDeleteResult.Value!.Id);
                    importJobResult.ContainersDeleted.Add((fedoraDeleteResult.Value as Container)!);
                }
                else
                {
                    return await FailEarly(fedoraDeleteResult.CodeAndMessage());
                }
                await KeepTransactionAlive();
            }
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Caught error in importJob, rolling back transaction");
            await fedoraClient.RollbackTransaction(transaction);
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            importJobResult.Errors = [new Error { Message = ex.Message }];
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }

        logger.LogInformation("Commiting Fedora transaction " + transaction.Location);
        await fedoraClient.CommitTransaction(transaction);
        importJobResult.DateFinished = DateTime.UtcNow;
        importJobResult.Status = ImportJobStates.Completed;
        return Result.OkNotNull(importJobResult);

        
        async Task<Result<ImportJobResult>> FailEarly(string? errorMessage)
        {
            logger.LogError("Failing Import Job Early: {errorMessage}", errorMessage);
            await fedoraClient.RollbackTransaction(transaction);
            importJobResult.Errors = [new Error { Message = errorMessage ?? "" }];
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }

        async Task KeepTransactionAlive()
        {
            // Fedora's default transaction timeout is 3 minutes
            // We will poke the transaction after 60s - but if a single operation takes > 2m it will still time out.
            if (timer.ElapsedMilliseconds > 60000)
            {
                logger.LogInformation("Keeping transaction alive after {elapsedMilliseconds} ms", timer.ElapsedMilliseconds);
                await fedoraClient.KeepTransactionAlive(transaction);
                timer.Restart();
            }
            // We could save the current state of the Result here...
        }
    }
}