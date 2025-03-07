using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Features.Import;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Activity.Requests;

public class GetImportJobsOrderedCollection : IRequest<Result<OrderedCollection>> { }

public class GetImportJobsOrderedCollectionHandler(
    IImportJobResultStore importJobResultStore,
    Converters converters) : IRequestHandler<GetImportJobsOrderedCollection, Result<OrderedCollection>>
{
    public async Task<Result<OrderedCollection>> Handle(GetImportJobsOrderedCollection request, CancellationToken cancellationToken)
    {
        var totalItemsResult = await importJobResultStore.GetTotalImportJobs(cancellationToken);
        if (totalItemsResult is not { Success: true, Value: > 0 })
        {
            return Result.FailNotNull<OrderedCollection>(ErrorCodes.UnknownError, totalItemsResult.ErrorMessage);
        }
        var id = converters.ActivityUri("importjobs/collection");
        int totalPages = totalItemsResult.Value / 100;
        var collection = new OrderedCollection
        {
            Id = id,
            First = new OrderedCollectionPage
            {
                Id = converters.ActivityUri("importjobs/pages/0")
            },
            Last =  new OrderedCollectionPage
            {
                Id = converters.ActivityUri($"importjobs/pages/{totalPages - 1}")
            },
            TotalItems = totalItemsResult.Value
        };
        return Result.OkNotNull(collection);
    }
}