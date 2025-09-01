using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class SearchRequest(string text, int page = 0, int pageSize = 50) : IRequest<Result<SearchCollection?>>
{
    public string Text { get; } = text;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;

}


public class SearchRequestHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<SearchRequest, Result<SearchCollection?>>
{
    public async Task<Result<SearchCollection?>> Handle(SearchRequest request, CancellationToken cancellationToken)
    {
        var returnValue = new SearchCollection();

        var fedoraResult = await storageApiClient.FedoraSearch(request.Text, request.Page, request.PageSize);
        if (fedoraResult.Value is not null)
        {
            returnValue.FedoraSearch = fedoraResult.Value;
        }

        //Add other search sources here

        return Result.Ok(returnValue);
    }
}
