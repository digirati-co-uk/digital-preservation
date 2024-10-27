using MediatR;
using Storage.API.Features.Import.Requests;

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
                await importJobResultStore.SaveImportJobResult(jobIdentifier, executeResult.Value!, cancellationToken);
                logger.LogInformation("Saving Import Job Result: " + executeResult.Value!.Id);
                // At this point we could broadcast a message
                return;
            }
            logger.LogError("Unable to execute Import Job Result: " + executeResult.CodeAndMessage());
        }
        logger.LogError("Unable to load Import Job: " + importJob.CodeAndMessage());
    }
}