using DigitalPreservation.Common.Model;
using MediatR;
using Storage.API.Features.Import.Requests;
using Storage.API.Features.Repository.Requests;

namespace Storage.API.Features.Import;

public class ImportJobRunner(
    ILogger<ImportJobRunner> logger,
    IMediator mediator,
    IImportJobResultStore importJobResultStore)
{
    public async Task Execute(string jobIdentifier, CancellationToken cancellationToken)
    {
        // Should all this go inside ExecuteImportJob?
        // It needs to save the ImportJobResult itself to update it.
        var importJob = await importJobResultStore.GetImportJob(jobIdentifier, cancellationToken);
        var initialResult = await importJobResultStore.GetImportJobResult(jobIdentifier, cancellationToken);
        if (importJob.Success && initialResult.Success)
        {
            var executeResult = await mediator.Send(new ExecuteImportJob(jobIdentifier, importJob.Value!, initialResult.Value!), cancellationToken);
            if (executeResult.Success)
            {
                var jobResult = executeResult.Value!;
                // The job itself may have failed at this point, but the result is still a success
                await importJobResultStore.SaveImportJobResult(jobIdentifier, jobResult, false, cancellationToken);
                // what version are we now on?
                var agResult = await mediator.Send(new GetResourceFromFedora(jobResult.ArchivalGroup.GetPathUnderRoot()), cancellationToken);
                if (agResult.Success)
                {
                    if (agResult.Value is ArchivalGroup ag)
                    {
                        jobResult.NewVersion = ag.Version!.OcflVersion;
                        await importJobResultStore.SaveImportJobResult(jobIdentifier, jobResult, false, cancellationToken);
                        logger.LogInformation("Saving Import Job Result: " + executeResult.Value!.Id);
                        // At this point we could broadcast a message
                        return;
                    }
                    logger.LogError("Resource is not an Archival Group: " + agResult.Value);
                    return;
                }
                logger.LogError("Unable to obtain saved Archival Group: " + agResult.CodeAndMessage());
                return;
            }
            logger.LogError("Unable to execute Import Job Result: " + executeResult.CodeAndMessage());
            return;
        }
        logger.LogError("Unable to load Import Job: " + importJob.CodeAndMessage());
    }
}