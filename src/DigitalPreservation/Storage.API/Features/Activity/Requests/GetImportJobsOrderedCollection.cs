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
            if (totalItemsResult.Failure)
            {
                // Diverged (issue #388): this used to check Value: > 0 as well as Success, so an
                // empty-but-successful stream (no import jobs at all) was reported as a 500 with a
                // null Detail, instead of the empty collection the Preservation API's own copy of
                // this logic already returned for the identical condition.
                return Result.ConvertFailNotNull<int, OrderedCollection>(totalItemsResult);
            }

            var collection = OrderedCollection.For(
                converters.ActivityUri("importjobs/collection"),
                p => converters.ActivityUri($"importjobs/pages/{p}"),
                totalItemsResult.Value);

            return Result.OkNotNull(collection);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get import jobs ordered collection");
            return Result.FailNotNull<OrderedCollection>(ErrorCodes.UnknownError, "Could not get import jobs ordered collection: " + e.Message);
        }
    }
}