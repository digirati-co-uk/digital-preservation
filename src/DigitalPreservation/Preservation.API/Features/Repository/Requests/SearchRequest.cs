using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using System.Threading;



namespace Preservation.API.Features.Repository.Requests;

public class SearchRequest(string text, int page = 0, int pageSize = 50, SearchType type = SearchType.All, int otherPage = 0) : IRequest<Result<SearchCollection?>>
{
    public string Text { get; } = text;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;
    public SearchType Type { get; } = type;
    public int OtherPage { get; } = otherPage;

}


public class SearchRequestHandler(
    IStorageApiClient storageApiClient,
    PreservationContext dbContext,
    IIdentityService identityService,
    ResourceMutator resourceMutator) : IRequestHandler<SearchRequest, Result<SearchCollection?>>
{
    public async Task<Result<SearchCollection?>> Handle(SearchRequest request, CancellationToken cancellationToken)
    {
        var returnValue = new SearchCollection();
        SearchCollectiveFedora? fedoraResult = null;
        Identifier? identifier = null;
        SearchCollectiveDeposit? depositSearch = null;


        fedoraResult = await GetFedoraResults(request);
        depositSearch = await GetDeposits(request);
        identifier = await GetIdentifier(request, cancellationToken);

         
        returnValue.FedoraSearch = fedoraResult;
        returnValue.DepositSearch = depositSearch;
        returnValue.Identifier = identifier;

        return Result.Ok(returnValue);
    }

    private async Task<SearchCollectiveFedora?> GetFedoraResults(SearchRequest request)
    {
        var page = request.Type is SearchType.Fedora or SearchType.All ? request.Page : request.OtherPage;
        var fedoraResult = await storageApiClient.FedoraSearch(request.Text, page, request.PageSize);
        if (fedoraResult is { Success: true, Value: not null })
        {
            return fedoraResult.Value;
        }
        return null;
    }

    private async Task<Identifier?> GetIdentifier(SearchRequest request, CancellationToken cancellationToken)
    {
        // Identifier search
        var sv = new SchemaAndValue()
        {
            Schema = "pid",
            Value = request.Text
        };
        var identityResult = await identityService.GetIdentityBySchema(sv, cancellationToken);
        if (identityResult is { Success: true, Value: not null })
        {
            var record = identityResult.Value;
            return resourceMutator.MutateIdentityRecord(identityResult.Value);
        }
        return null;
    }

    private async Task<SearchCollectiveDeposit?> GetDeposits(SearchRequest request)
    {
        var page = request.Type is SearchType.Deposits or SearchType.All ? request.Page : request.OtherPage;
        var depositsDb = dbContext.Deposits
            .Where(d => d.ArchivalGroupName!.Contains(request.Text) ||
                        (d.SubmissionText!.Contains(request.Text) ||
                         (d.ArchivalGroupPathUnderRoot!.Contains(request.Text)) ||
                         (d.MintedId == request.Text)
                        ))
            .Skip(page * request.PageSize)
            .Take(request.PageSize).ToList();
        
        if (!depositsDb.Any())
        {
            return null;
        }
  
        var count = dbContext.Deposits
            .Count(d => d.ArchivalGroupName!.Contains(request.Text) ||
                        (d.SubmissionText!.Contains(request.Text) ||
                         (d.ArchivalGroupPathUnderRoot!.Contains(request.Text)) ||
                         (d.MintedId == request.Text)
                        ));


        var result = new SearchCollectiveDeposit()
        {
            Deposits = resourceMutator.MutateDeposits(depositsDb),
            Page = page,
            PageSize = request.PageSize,
            Total = count
        };

        return result;
    }
}
