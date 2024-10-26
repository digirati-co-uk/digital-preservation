using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public class ExecuteImportJob(ImportJob importJob) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
}

public class ExecuteImportJobHandler(
    ILogger<ExecuteImportJobHandler> logger,
    IFedoraClient fedoraClient,
    Converters converters) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(ExecuteImportJob request, CancellationToken cancellationToken)
    {
        var callerIdentity = "dlipdev";
        var importJob = request.ImportJob;
        
        var start = DateTime.UtcNow;
        
        var transaction = await fedoraClient.BeginTransaction();
        var archivalGroupPathUnderRoot = importJob.ArchivalGroup.GetPathUnderRoot()!;
        var validationResult = await fedoraClient.GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot, transaction);
        if (validationResult.Failure)
        {
            return Result.ConvertFailNotNull<ArchivalGroup?, ImportJobResult>(validationResult);
        }

        var archivalGroup = validationResult.Value;
        if (!importJob.IsUpdate)
        {
            if(archivalGroup != null)
            {
                await fedoraClient.RollbackTransaction(transaction);
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.Conflict, "An Archival Group has recently been created at " + archivalGroupPathUnderRoot);
            }

            if (string.IsNullOrWhiteSpace(importJob.ArchivalGroupName))
            {
                await fedoraClient.RollbackTransaction(transaction);
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
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "No archival group was returned from creation");
            }
        }
        
        // We need to keep the transaction alive throughout this process
        // will need to time operations and call fedora.KeepTransactionAlive

        var importJobResult = new ImportJobResult
        {
            Id = converters.GetStorageImportJobResultId(archivalGroupPathUnderRoot, transaction.Location.GetSlug()!),
            Status = ImportJobStates.Running,
            ArchivalGroup = importJob.ArchivalGroup,
            DateBegun = start,
            Created = start,
            CreatedBy = converters.GetAgentUri(callerIdentity),
            LastModified = start,
            LastModifiedBy = converters.GetAgentUri(callerIdentity),
            ImportJob = importJob.Id!, // NB this may be the Preservation API's import job ID - which is OK
            OriginalImportJob = importJob.Id!, // what's the purpose of these when there's always a RESULT... are they the same?
        };
        
        try
        {
            foreach (var container in importJob.ContainersToAdd.OrderBy(cd => cd.Id))
            {
                logger.LogInformation("Creating container {id}", container.Id);
                var fedoraContainerResult = await fedoraClient.CreateContainer(container.Id.GetPathUnderRoot()!, container.Name, transaction, cancellationToken: cancellationToken);
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
            }

            // patch files
            // This is EXACTLY the same as Add / PUT.
            // We will need to accomodate some RDF updates - but nothing that can't be carried on BinaryFile
            // nothing _arbitrary_
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
            }

            // delete files
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
            }


            // delete containers
            // Should we verify that the container is empty first?
            // Do we want to allow deletion of non-empty containers? It wouldn't come from a diff importJob
            // but might come from other importJob use.
            foreach (var container in importJob.ContainersToDelete)
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
            }
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Caught error in importJob, rolling back transaction");
            await fedoraClient.RollbackTransaction(transaction);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, ex.Message);
        }

        await fedoraClient.CommitTransaction(transaction);
        importJobResult.DateFinished = DateTime.UtcNow;
        return Result.OkNotNull(importJobResult);

        
        async Task<Result<ImportJobResult>> FailEarly(string? errorMessage)
        {
            await fedoraClient.RollbackTransaction(transaction);
            importJobResult.Errors = [new Error { Message = errorMessage ?? "" }];
            importJobResult.DateFinished = DateTime.UtcNow;
            importJobResult.Status = ImportJobStates.CompletedWithErrors;
            return Result.OkNotNull(importJobResult); // This is a "success" for the purposes of returning an ImportJobResult
        }
    }
}