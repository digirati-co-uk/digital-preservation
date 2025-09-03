using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.Common.Model.Identity;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
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
    PreservationContext dbContext,
    IIdentityService identityService,
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

        //Add other search sources
        //

        returnValue.DepositSearch = new SearchCollectiveDeposit()
        {
            Deposits = [],
            Page = request.Page,
            PageSize = request.PageSize,
            Total = 0
        };

        var depositsDb = dbContext.Deposits
            .Where(d => d.ArchivalGroupName!.Contains(request.Text) ||
                        (d.SubmissionText!.Contains(request.Text) ||
                         (d.ArchivalGroupPathUnderRoot!.Contains(request.Text)) ||
                         (d.MintedId == request.Text)
                        ))
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize).ToList();
        

        if (depositsDb.Any())
        {
            var deposits = resourceMutator.MutateDeposits(depositsDb);
            returnValue.DepositSearch.Deposits = deposits;
            returnValue.DepositSearch.Total = deposits.Count;
            returnValue.DepositSearch.Page = request.Page;
            returnValue.DepositSearch.PageSize = request.PageSize;

        }

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
            returnValue.Identifier = resourceMutator.MutateIdentityRecord(identityResult.Value);
        }

        return Result.Ok(returnValue);
    }
}
