using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Features.Import;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Activity.Requests;

public class GetImportJobsOrderedCollectionPage(int page) : IRequest<Result<OrderedCollectionPage>>
{
    public int Page { get; } = page;
}

public class GetImportJobsOrderedCollectionPageHandler(
    ILogger<GetImportJobsOrderedCollectionPageHandler> logger,
    IImportJobResultStore importJobResultStore,
    Converters converters) : IRequestHandler<GetImportJobsOrderedCollectionPage, Result<OrderedCollectionPage>>
{
    public async Task<Result<OrderedCollectionPage>> Handle(GetImportJobsOrderedCollectionPage request, CancellationToken cancellationToken)
    {
        if (request.Page < 1)
        {
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.BadRequest, "Page must be 1 or greater.");
        }

        try
        {
            var totalItemsResult = await importJobResultStore.GetTotalImportJobs(cancellationToken);
            if (totalItemsResult.Failure)
            {
                // Diverged (issue #388): this used to check Value: > 0 as well as Success, so an
                // empty-but-successful stream would be reported as a 500 with a null Detail.
                return Result.ConvertFailNotNull<int, OrderedCollectionPage>(totalItemsResult);
            }

            var pageResult = await importJobResultStore.GetActivityPageOfResults(
                request.Page,
                OrderedCollectionPage.DefaultPageSize,
                cancellationToken);
            if (pageResult is not { Success: true, Value: not null })
            {
                return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, pageResult.ErrorMessage);
            }

            var page = OrderedCollectionPage.For(
                converters.ActivityUri("importjobs/collection"),
                p => converters.ActivityUri($"importjobs/pages/{p}"),
                request.Page,
                totalItemsResult.Value,
                pageResult.Value);

            return Result.OkNotNull(page);

        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get import jobs ordered collection");
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, "Could not get import jobs collection page: " + e.Message);
        }
    }
}