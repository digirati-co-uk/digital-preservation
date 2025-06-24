using System.Diagnostics;
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
    IFedoraClient fedoraClient) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
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
        var transactionMonitor = new FedoraTransactionMonitor(logger, fedoraClient, transaction, stopwatch);
        var timer = new Timer(transactionMonitor.MaintainTransactionState, transaction, 60 * 1000, 60 * 1000);

        logger.LogInformation("(TX) Fedora transaction begun: " + transaction.Location);
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
            logger.LogInformation("Archival Group version: " + sourceVersion);
        }
        
        importJobResult.SourceVersion = sourceVersion;
        
        logger.LogInformation("Saving running ImportJobResult before processing binaries and containers");
        await importJobResultStore.SaveImportJobResult(
            request.JobIdentifier, importJobResult, true, false, cancellationToken);

        
        logger.LogInformation("(TX) Now looping through import job tasks");
        try
        {
            logger.LogInformation("{count} containers to add", importJob.ContainersToAdd.Count);
            foreach (var container in importJob.ContainersToAdd.OrderBy(cd => cd.Id!.ToString()))
            {
                logger.LogInformation("(TX) Creating container {id}", container.Id);
                var fedoraContainerResult = await fedoraClient.CreateContainerWithinArchivalGroup(
                    container.Id.GetPathUnderRoot()!,
                    callerIdentity,
                    container.Name, transaction, cancellationToken: cancellationToken);
                if (fedoraContainerResult.Success)
                {
                    logger.LogInformation("Container created at {location}", fedoraContainerResult.Value!.Id);
                    importJobResult.ContainersAdded.Add(fedoraContainerResult.Value!);
                }
                else
                {
                    return await FailEarly(fedoraContainerResult.CodeAndMessage());
                }
            }

            // what about deletions of containers? conflict?

            // create files
            logger.LogInformation("{count} binaries to add", importJob.BinariesToAdd.Count);
            foreach (var binary in importJob.BinariesToAdd)
            {
                logger.LogInformation("(TX) Adding binary {id}, size: {size}", binary.Id, StringUtils.FormatFileSize(binary.Size));
                var fedoraPutBinaryResult = await fedoraClient.PutBinary(
                    binary,
                    callerIdentity,
                    transaction, cancellationToken);
                if (fedoraPutBinaryResult.Success)
                {
                    logger.LogInformation("Binary created at {location}", fedoraPutBinaryResult.Value!.Id);
                    importJobResult.BinariesAdded.Add(fedoraPutBinaryResult.Value!);
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
            logger.LogInformation("{count} binaries to patch", importJob.BinariesToPatch.Count);
            foreach (var binary in importJob.BinariesToPatch)
            {
                logger.LogInformation("(TX) Patching file {id}, size: {size}", binary.Id, StringUtils.FormatFileSize(binary.Size));
                var fedoraPatchBinaryResult = await fedoraClient.PutBinary(
                    binary,
                    callerIdentity,
                    transaction,
                    cancellationToken);
                if (fedoraPatchBinaryResult.Success)
                {
                    logger.LogInformation("Binary patched at {location}", fedoraPatchBinaryResult.Value!.Id);
                    importJobResult.BinariesPatched.Add(fedoraPatchBinaryResult.Value!);
                }
                else
                {
                    return await FailEarly(fedoraPatchBinaryResult.CodeAndMessage());
                }
            }

            // delete files
            logger.LogInformation("{count} binaries to delete", importJob.BinariesToDelete.Count);
            foreach (var binary in importJob.BinariesToDelete)
            {
                logger.LogInformation("(TX) Deleting file {id}", binary.Id);
                var fedoraDeleteResult = await fedoraClient.Delete(
                    binary,
                    callerIdentity,
                    transaction,
                    cancellationToken);
                if (fedoraDeleteResult.Success)
                {
                    logger.LogInformation("Binary deleted at {location}", fedoraDeleteResult.Value!.Id);
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
            logger.LogInformation("{count} containers to delete", importJob.ContainersToDelete.Count);
            foreach (var container in importJob.ContainersToDelete.OrderByDescending(c => c.Id!.ToString()))
            {
                logger.LogInformation("(TX) Deleting container {id}", container.Id);
                var fedoraDeleteResult = await fedoraClient.Delete(
                    container,
                    callerIdentity,
                    transaction,
                    cancellationToken);
                if (fedoraDeleteResult.Success)
                {
                    logger.LogInformation("Container deleted at {location}", fedoraDeleteResult.Value!.Id);
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
            logger.LogError(ex, "(TX) Caught error in importJob, rolling back transaction");
            await fedoraClient.RollbackTransaction(transaction);
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            importJobResult.Errors = [new Error { Message = ex.Message }];
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }

        logger.LogInformation("(TX) Commiting Fedora transaction " + transaction.Location);
        var startCommitTime = DateTime.UtcNow;
        try
        {
            await transactionMonitor.CommitTransaction();
            await timer.DisposeAsync(); // does this stop the timer?
        }
        catch (Exception e)
        {
            await timer.DisposeAsync(); // does this stop the timer?
            var errorTime = DateTime.UtcNow - startCommitTime;
            var message = $"(TX) Unable to commit Fedora transaction: duration {errorTime.TotalSeconds} seconds: {e.Message}";
            logger.LogError(e, message);
            return await FailEarly(message, rollback: false);
        }
        importJobResult.DateFinished = DateTime.UtcNow;
        var commitDuration = importJobResult.DateFinished - startCommitTime;
        logger.LogInformation("(TX) Fedora commit transaction took {duration} seconds", commitDuration.Value.TotalSeconds);
        importJobResult.Status = ImportJobStates.Completed;
        return Result.OkNotNull(importJobResult);

        
        async Task<Result<ImportJobResult>> FailEarly(string? errorMessage, string? errorCode = ErrorCodes.UnknownError, bool rollback = true)
        {
            await timer.DisposeAsync();
            logger.LogError("(TX) Failing Import Job Early: {errorCode} - {errorMessage}", errorCode, errorMessage);
            if (rollback)
            {
                await fedoraClient.RollbackTransaction(transaction);
            }
            importJobResult.Errors = [new Error { Message = errorMessage ?? "" }];
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }
    }

    private Result PreProcessValidateImportJob(ImportJob importJob)
    {
        var allBinaries = 
            importJob.BinariesToAdd
            .Union(importJob.BinariesToPatch)
            .Union(importJob.BinariesToRename)
            .Union(importJob.BinariesToDelete);
        // Can add more to this later...
        foreach (var binary in allBinaries)
        {
            if (binary.Id == null)
            {
                return Result.Fail(ErrorCodes.BadRequest, "Binary ID is null");
            }
            if (binary.Id.LocalPath.Contains('#'))
            {
                return Result.Fail(ErrorCodes.BadRequest, $"Binary ID contains a fragment identifier (#): {binary.Id}");
            }
        }
        return Result.Ok();
    }
}