using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class SearchRequest(string text, int page = 0, int pageSize = 50) : IRequest<Result<SearchCollection?>>
{
    public string Text { get; } = text;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
}

public class SearchRequestHandler(IPreservationApiClient preservationApiClient) 
    : IRequestHandler<SearchRequest, Result<SearchCollection?>>
{
    public async Task<Result<SearchCollection?>> Handle(SearchRequest request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.Search(request.Text, request.Page, request.PageSize);
    }
}


