using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.ChangeDiscovery.Activities;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Activity.Requests;

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
        var totalItems = await dbContext.ArchivalGroupEvents.CountAsync(cancellationToken: cancellationToken);
        
        try
        {
            var entities = await dbContext.ArchivalGroupEvents
                .OrderBy(e => e.EventDate)
                .Skip((request.Page - 1) * OrderedCollectionPage.DefaultPageSize)
                .Take(OrderedCollectionPage.DefaultPageSize)
                .ToListAsync(cancellationToken);

            var activities = entities
                .Select(MakeActivity)
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
            logger.LogError(e, e.Message);
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    private static DigitalPreservation.Common.Model.ChangeDiscovery.Activity MakeActivity(ArchivalGroupEvent entity)
    {
        // TODO deletions
        var agObject = new ActivityObject
        {
            Id = entity.ArchivalGroup,
            Type = nameof(ArchivalGroup),
            SeeAlso =
            [
                new ActivityObject
                {
                    Id = entity.ArchivalGroup,
                    Type = nameof(ImportJobResult)
                }
            ]
        };
        if (entity.FromVersion is null)
        {
            return new Create { Object = agObject };
        }

        return new Update { Object = agObject };
    }
    
}