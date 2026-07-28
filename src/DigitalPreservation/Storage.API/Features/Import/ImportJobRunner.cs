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
                var pathUnderRoot = jobResult.ArchivalGroup.GetPathUnderRoot();
                // The job itself may have failed at this point, but the result is still a success
                // what version are we now on? This may be unchanged if the job itself failed
                logger.LogInformation("ImportJobRunner.Execute for {JobIdentifier} has returned, will try to obtain {PathUnderRoot} from Fedora.",
                    jobIdentifier, pathUnderRoot);
                var agResult = await mediator.Send(new GetResourceFromFedora(pathUnderRoot), cancellationToken);
                if (agResult.Success)
                {
                    if (agResult.Value is ArchivalGroup ag)
                    {
                        jobResult.NewVersion = ag.Version!.OcflVersion;
                        logger.LogInformation("Import Job new version is {NewVersion} for {JobResultId}", jobResult.NewVersion, jobResult.Id);
                    }
                    else
                    {
                        logger.LogError("Resource is not an Archival Group: {Resource}", agResult.Value);
                    }
                }
                else
                {
                    logger.LogError("Unable to obtain saved Archival Group (maybe because of a failed create): {CodeAndMessage}", agResult.CodeAndMessage());
                }
                var finalUpdateResult = await importJobResultStore.SaveImportJobResult(jobIdentifier, jobResult, false, true, cancellationToken);
                if (finalUpdateResult.Success)
                {
                    logger.LogInformation("Saved Import Job Result: {JobResultId}", jobResult.Id);
                }
                else
                {
                    logger.LogError("Failed to update final import job: {JobResultId}, {CodeAndMessage}", jobResult.Id, finalUpdateResult.CodeAndMessage());
                }
                return;
            }
            logger.LogError("Unable to execute Import Job Result, and did not fail early cleanly: {CodeAndMessage}", executeResult.CodeAndMessage());
            return;
        }
        logger.LogError("Unable to load Import Job: {CodeAndMessage}", importJob.CodeAndMessage());
    }
}