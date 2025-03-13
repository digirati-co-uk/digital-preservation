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
    IImportJobResultStore importJobResultStore,
    Converters converters) : IRequestHandler<GetImportJobsOrderedCollectionPage, Result<OrderedCollectionPage>>
{
    public async Task<Result<OrderedCollectionPage>> Handle(GetImportJobsOrderedCollectionPage request, CancellationToken cancellationToken)
    {
        var totalItemsResult = await importJobResultStore.GetTotalImportJobs(cancellationToken);
        if (totalItemsResult is not { Success: true, Value: > 0 })
        {
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, totalItemsResult.ErrorMessage);
        }
        var pageResult = await importJobResultStore.GetActivityPageOfResults(
            request.Page, 
            OrderedCollectionPage.DefaultPageSize, 
            cancellationToken);
        if (pageResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, pageResult.ErrorMessage);
        }

        int startIndex = (request.Page - 1) * OrderedCollectionPage.DefaultPageSize;
        int totalItems = totalItemsResult.Value;
        var page = new OrderedCollectionPage
        {
            Id = converters.ActivityUri($"importjobs/pages/{request.Page}"),
            PartOf = new OrderedCollection
            {
                Id = converters.ActivityUri("importjobs/collection"),
            },
            StartIndex = startIndex,
            OrderedItems = pageResult.Value
        };
        if (request.Page > 1)
        {
            page.Prev = new OrderedCollectionPage
            {
                Id = converters.ActivityUri($"importjobs/pages/{request.Page - 1}"),
            };
        }
        if (totalItems > startIndex + OrderedCollectionPage.DefaultPageSize)
        {
            page.Next = new OrderedCollectionPage
            {
                Id = converters.ActivityUri($"importjobs/pages/{request.Page + 1}"),
            };
        }
        page.WithContext();
        
        return Result.OkNotNull(page);
    }
}