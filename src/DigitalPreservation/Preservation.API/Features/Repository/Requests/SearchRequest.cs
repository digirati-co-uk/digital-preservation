using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;


namespace Preservation.API.Features.Repository.Requests;

public class SearchRequest(string text, int page = 0, int pageSize = 20, SearchType type = SearchType.All, int otherPage = 0) : IRequest<Result<SearchCollection?>>
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
        var fedoraResult = await GetFedoraResults(request);
        var identifier = await GetIdentifier(request, cancellationToken);
        var depositSearch = await GetDeposits(request);
        
        returnValue.FedoraSearch = fedoraResult;
        returnValue.DepositSearch = depositSearch;
        returnValue.Identifier = identifier;

        return Result.Ok(returnValue);
    }

    private async Task<SearchCollectiveFedora?> GetFedoraResults(SearchRequest request)
    {
        var page = request.Type is SearchType.Fedora or SearchType.All ? request.Page : request.OtherPage;
        var fedoraResult = await storageApiClient.FedoraSearch(request.Text, page, request.PageSize);
        return fedoraResult is { Success: true, Value: not null } ? fedoraResult.Value : null;
    }

    private async Task<Identifier?> GetIdentifier(SearchRequest request, CancellationToken cancellationToken)
    {
        // Identifier search get first matchind Id or CatIRN
        var sv = new SchemaAndValue()
        {
            Schema = "pid",
            Value = request.Text
        };
        var identityResult = await identityService.GetIdentityBySchema(sv, cancellationToken);

        if (identityResult.Value is null)
            identityResult = await identityService.GetIdentityByCatIrn(request.Text, cancellationToken);

        return identityResult is { Success: true, Value: not null } ? resourceMutator.MutateIdentityRecord(identityResult.Value) : null;
    }

    private Task<SearchCollectiveDeposit?> GetDeposits(SearchRequest request)
    {
        var page = request.Type is SearchType.Deposits or SearchType.All ? request.Page : request.OtherPage;
        var depositsDb = dbContext.Deposits
            .Where(d =>
                d.ArchivalGroupName!.ToLower().Contains(request.Text.ToLower()) ||
                d.SubmissionText!.ToLower().Contains(request.Text.ToLower()) ||
                d.ArchivalGroupPathUnderRoot!.ToLower().Contains(request.Text.ToLower()) ||
                d.MintedId.ToLower().Contains(request.Text.ToLower())
            )
            .OrderByDescending(o => o.Created)
            .Skip(page * request.PageSize)
            .Take(request.PageSize).ToList();

        if (!depositsDb.Any())
        {
            return Task.FromResult<SearchCollectiveDeposit?>(null);
        }

        var count = dbContext.Deposits
            .Count(d => d.ArchivalGroupName!.ToLower().Contains(request.Text.ToLower()) ||
                        (d.SubmissionText!.ToLower().Contains(request.Text.ToLower()) ||
                         (d.ArchivalGroupPathUnderRoot!.ToLower().Contains(request.Text.ToLower())) ||
                         (d.MintedId.ToLower().Contains(request.Text.ToLower()))
                        ));

        var result = new SearchCollectiveDeposit()
        {
            Deposits = resourceMutator.MutateDeposits(depositsDb),
            Page = page,
            PageSize = request.PageSize,
            Total = count
        };

        return Task.FromResult(result)!;
    }
}
