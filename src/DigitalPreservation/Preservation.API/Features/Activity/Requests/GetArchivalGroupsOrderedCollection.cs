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
        var totalItems = await dbContext.PublishedArchivalGroupEvents()
            .CountAsync(cancellationToken: cancellationToken);

        var collection = OrderedCollection.For(
            resourceMutator.GetActivityStreamUri("archivalgroups/collection"),
            p => resourceMutator.GetActivityStreamUri($"archivalgroups/pages/{p}"),
            totalItems);

        return Result.OkNotNull(collection);
    }
}