using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using MediatR;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

public class SearchFromFedoraSimple(string text, int? page, int? pageSize) : IRequest<Result<SearchCollectiveFedora?>>
{
    public string Text { get; } = text;
    public int? Page { get; } = page;
    public int? PageSize { get; } = pageSize;
}


public class SearchFromFedoraSimpleHandler(IFedoraClient fedoraClient) : IRequestHandler<SearchFromFedoraSimple, Result<SearchCollectiveFedora?>>
{
    public async Task<Result<SearchCollectiveFedora?>> Handle(SearchFromFedoraSimple request, CancellationToken cancellationToken)
    {
        return await fedoraClient.GetBasicSearchResults(request.Text, request.Page, request.PageSize, cancellationToken);
    }
}
