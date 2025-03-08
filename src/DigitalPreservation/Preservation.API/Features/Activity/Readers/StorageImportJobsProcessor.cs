using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Features.ImportJobs.Requests;
using Storage.Client;

namespace Preservation.API.Features.Activity.Readers;

public class StorageImportJobsProcessor(
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ILogger<StorageImportJobsProcessor> logger,
    IMediator mediator)
{
    public async Task<Result> ReadStream(CancellationToken cancellationToken)
    {
        // Need to see when we last updated our own activity stream, then read to that point
        var latestEvent = dbContext.ArchivalGroupEvents
            .OrderByDescending(e => e.EventDate)
            .FirstOrDefault();
        if (latestEvent == null)
        {
            logger.LogWarning("Unable to obtain latest event date from ArchivalGroupEvents");
            return Result.Ok();  // still going to return OK here
        }
        logger.LogInformation("Latest recorded ArchivalGroupEvent was at {eventDate}", latestEvent.EventDate);
        var activitiesResult = await storageApiClient.GetImportJobActivities(latestEvent.EventDate, cancellationToken);
        if (activitiesResult is not { Success: true, Value: not null })
        {
            return Result.Fail(activitiesResult.ErrorCode!, activitiesResult.ErrorMessage);
        }
        logger.LogInformation("{count} activities returned from GetImportJobActivities", activitiesResult.Value.Count);
        foreach (var activity in activitiesResult.Value)
        {
            var jobEntity = dbContext.GetImportJobFromStorageImportJobResult(activity.Object.Id);
            if (jobEntity == null)
            {
                logger.LogError("No import job found in DB for {importJobResult}", activity.Object.Id);
                continue;
            }
            // This will also update our local record
            var fullJobResult = await mediator.Send(
                new GetImportJobResult(jobEntity.Deposit, jobEntity.Id), cancellationToken);
            if (fullJobResult is not { Success: true, Value: not null })
            {
                logger.LogError("Unable to get full import job result for {deposit}, {id}", jobEntity.Deposit, jobEntity.Id);
                continue;
            }
            var fullJob = fullJobResult.Value;
            if (fullJob.DateFinished is null)
            {
                logger.LogError("Job for {deposit}, {id} does not have a DateFinished", jobEntity.Deposit, jobEntity.Id);
                continue;
            }
            // TODO: Deletions
            var agEvent = new ArchivalGroupEvent
            {
                EventDate = fullJob.DateFinished.Value,
                ArchivalGroup = fullJob.ArchivalGroup,
                ImportJobResult = activity.Object.Id,
                FromVersion = fullJob.SourceVersion,
                ToVersion = fullJob.NewVersion
            };
            dbContext.ArchivalGroupEvents.Add(agEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }
}