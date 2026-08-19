using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Activity.Requests;

public class GetArchivalGroupsOrderedCollection : IRequest<Result<OrderedCollection>> { }


public class GetImportJobsOrderedCollectionHandler(
    ResourceMutator resourceMutator,
    PreservationContext dbContext) : IRequestHandler<GetArchivalGroupsOrderedCollection, Result<OrderedCollection>>
{
    public async Task<Result<OrderedCollection>> Handle(GetArchivalGroupsOrderedCollection request, CancellationToken cancellationToken)
    {
        // Published events only; see the note on the page handler about keeping the two in step.
        var totalItems = await dbContext.ArchivalGroupEvents
            .Where(e => !e.Suppressed)
            .CountAsync(cancellationToken: cancellationToken);
        int totalPages = totalItems / OrderedCollectionPage.DefaultPageSize;
        if (totalItems % OrderedCollectionPage.DefaultPageSize > 0)
        {
            totalPages++;
        }
        var collection = new OrderedCollection
        {
            Id = resourceMutator.GetActivityStreamUri("archivalgroups/collection"),
            First = new OrderedCollectionPage
            {
                Id = resourceMutator.GetActivityStreamUri("archivalgroups/pages/1")
            },
            Last =  new OrderedCollectionPage
            {
                Id = resourceMutator.GetActivityStreamUri($"archivalgroups/pages/{totalPages}")
            },
            TotalItems = totalItems
        };
        collection.WithContext();
        return Result.OkNotNull(collection);
    }
}