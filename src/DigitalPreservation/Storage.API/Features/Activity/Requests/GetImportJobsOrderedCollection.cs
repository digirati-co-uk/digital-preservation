using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Features.Import;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Activity.Requests;

public class GetImportJobsOrderedCollection : IRequest<Result<OrderedCollection>> { }

public class GetImportJobsOrderedCollectionHandler(
    ILogger<GetImportJobsOrderedCollectionHandler> logger,
    IImportJobResultStore importJobResultStore,
    Converters converters) : IRequestHandler<GetImportJobsOrderedCollection, Result<OrderedCollection>>
{
    public async Task<Result<OrderedCollection>> Handle(GetImportJobsOrderedCollection request, CancellationToken cancellationToken)
    {
        try
        {
            var totalItemsResult = await importJobResultStore.GetTotalImportJobs(cancellationToken);
            if (totalItemsResult is not { Success: true, Value: > 0 })
            {
                return Result.FailNotNull<OrderedCollection>(ErrorCodes.UnknownError, totalItemsResult.ErrorMessage);
            }

            var id = converters.ActivityUri("importjobs/collection");
            int totalPages = totalItemsResult.Value / OrderedCollectionPage.DefaultPageSize;
            if (totalItemsResult.Value % OrderedCollectionPage.DefaultPageSize > 0)
            {
                totalPages++;
            }

            var collection = new OrderedCollection
            {
                Id = id,
                First = new OrderedCollectionPage
                {
                    Id = converters.ActivityUri("importjobs/pages/1")
                },
                Last = new OrderedCollectionPage
                {
                    Id = converters.ActivityUri($"importjobs/pages/{totalPages}")
                },
                TotalItems = totalItemsResult.Value
            };
            collection.WithContext();
            return Result.OkNotNull(collection);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get import jobs ordered collection");
            return Result.FailNotNull<OrderedCollection>(ErrorCodes.UnknownError, "Could not get import jobs ordered collection: " + e.Message);
        }
    }
}