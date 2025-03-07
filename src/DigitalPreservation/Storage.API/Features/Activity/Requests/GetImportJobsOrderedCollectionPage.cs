using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
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
        
        Result<List<ImportJobResult>> pageResult = await importJobResultStore.GetActivityPageOfResults(request.Page, 100, cancellationToken);
        if (pageResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<OrderedCollectionPage>(ErrorCodes.UnknownError, pageResult.ErrorMessage);
        }
        var id = converters.ActivityUri($"importjobs/pages/{request.Page}");
        
        Build the page!!
        throw new NotImplementedException();
    }
}