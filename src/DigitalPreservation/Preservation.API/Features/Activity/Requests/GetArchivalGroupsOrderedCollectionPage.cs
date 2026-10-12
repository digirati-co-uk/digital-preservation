using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Activity.Requests;

using Activity = DigitalPreservation.Common.Model.ChangeDiscovery.Activity;
using ImportJobEntity = Preservation.API.Data.Entities.ImportJob;

public class GetArchivalGroupsOrderedCollectionPage(int page) : IRequest<Result<OrderedCollectionPage>>
{
    public int Page { get; } = page;
}

public class GetArchivalGroupsOrderedCollectionPageHandler(
    ILogger<GetArchivalGroupsOrderedCollectionPageHandler> logger,
    ResourceMutator resourceMutator,
    PreservationContext dbContext) : IRequestHandler<GetArchivalGroupsOrderedCollectionPage, Result<OrderedCollectionPage>>
{
    
    public async Task<Result<OrderedCollectionPage>> Handle(GetArchivalGroupsOrderedCollectionPage request, CancellationToken cancellationToken)
    {
        var totalItems = await dbContext.PublishedArchivalGroupEvents()
            .CountAsync(cancellationToken: cancellationToken);

        try
        {
            var entities = await dbContext.PublishedArchivalGroupEvents()
                .OrderBy(e => e.EventDate)
                .Skip((request.Page - 1) * OrderedCollectionPage.DefaultPageSize)
                .Take(OrderedCollectionPage.DefaultPageSize)
                .ToListAsync(cancellationToken);

            // entity.ImportJobResult is the raw Storage API result URI (StorageImportJobsProcessor
            // stores it verbatim); the seeAlso link Preservation API hands out has to resolve on
            // Preservation API instead (issue #265), which needs the deposit/import-job id pair that
            // only the ImportJobs table - not the event itself - knows.
            var importJobResultUris = entities
                .Where(e => e.ImportJobResult is not null)
                .Select(e => e.ImportJobResult!)
                .Distinct()
                .ToList();
            // Grouped rather than ToDictionaryAsync: nothing in the schema makes
            // StorageImportJobResultId unique, and a duplicate must not 500 a whole page of the
            // stream that iiif-builder polls unattended.
            var importJobsByResult = (await dbContext.ImportJobs
                    .Where(j => importJobResultUris.Contains(j.StorageImportJobResultId))
                    .ToListAsync(cancellationToken))
                .GroupBy(j => j.StorageImportJobResultId)
                .ToDictionary(g => g.Key, g => g.First());

            var activities = entities
                .Select(e => MakeActivity(e, importJobsByResult))
                .ToList();
            
            int startIndex = (request.Page - 1) * OrderedCollectionPage.DefaultPageSize;
            var page = new OrderedCollectionPage
            {
                Id = resourceMutator.GetActivityStreamUri($"archivalgroups/pages/{request.Page}"),
                PartOf = new OrderedCollection
                {
                    Id = resourceMutator.GetActivityStreamUri("archivalgroups/collection"),
                },
                StartIndex = startIndex,
                OrderedItems = activities
            };
            if (request.Page > 1)
            {
                page.Prev = new OrderedCollectionPage
                {
                    Id = resourceMutator.GetActivityStreamUri($"archivalgroups/pages/{request.Page - 1}")
                };
            }
            if (totalItems > startIndex + OrderedCollectionPage.DefaultPageSize)
            {
                page.Next = new OrderedCollectionPage
                {
                    Id = resourceMutator.GetActivityStreamUri($"archivalgroups/pages/{request.Page + 1}")
                };
            }
            page.WithContext();
            return Result.OkNotNull(page);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get OrderedCollectionPage");
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, e.Message);
        }
    }

    private Activity MakeActivity(ArchivalGroupEvent entity, Dictionary<Uri, ImportJobEntity> importJobsByResult)
    {
        // TODO deletions
        var activity = new Activity
        {
            Type = entity.FromVersion is null ? ActivityTypes.Create : ActivityTypes.Update,
            Object = new ActivityObject
            {
                Id = entity.ArchivalGroup,
                Type = nameof(ArchivalGroup)
            },
            EndTime = entity.EventDate
        };
        // Only set seeAlso when the import job that produced this event is still on record - never
        // fall back to the raw Storage API URI (issue #265), since nothing outside Storage API can
        // resolve it.
        if (entity.ImportJobResult is not null &&
            importJobsByResult.TryGetValue(entity.ImportJobResult, out var importJob))
        {
            activity.Object.SeeAlso =
            [
                new ActivityObject
                {
                    Id = resourceMutator.GetImportJobResultUri(importJob.Deposit, importJob.Id),
                    Type = nameof(ImportJobResult)
                }
            ];
        }

        return activity;
    }
}