using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDepositWithMets(string id, bool parse = true) : IRequest<Result<DepositWithMets>>
{
    public string Id { get; } = id;
    public bool Parse { get; } = parse;
}

public class GetDepositWithMetsHandler(
    IMetsParser metsParser,
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : 
        GetDepositBase(logger, dbContext, storageApiClient, resourceMutator), 
        IRequestHandler<GetDepositWithMets, Result<DepositWithMets>>
{
    public async Task<Result<DepositWithMets>> Handle(GetDepositWithMets request, CancellationToken cancellationToken)
    {
        var getDepositResult = await GetDeposit(request.Id, cancellationToken);
        if (getDepositResult.Success)
        { 
            var wrapperResult = await metsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!, request.Parse);
            if (wrapperResult.Success)
            {
                var deposit = getDepositResult.Value!;
                deposit.MetsETag = wrapperResult.Value?.ETag;
                return Result.OkNotNull(new DepositWithMets
                {
                    Deposit = getDepositResult.Value!,
                    MetsFileWrapper = wrapperResult.Value!
                });
            }
            return Result.FailNotNull<DepositWithMets>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);
        }
       
        return Result.FailNotNull<DepositWithMets>(getDepositResult.ErrorCode!, getDepositResult.ErrorMessage);
    }
}  