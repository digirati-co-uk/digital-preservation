using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Workspace;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDeposit(string id) : IRequest<Result<Deposit?>>
{
    public string Id { get; } = id;
}

public class GetDepositHandler(
    IMetsParser metsParser,
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory) : 
        GetDepositBase(logger, dbContext, storageApiClient, resourceMutator, workspaceManagerFactory), 
        IRequestHandler<GetDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(GetDeposit request, CancellationToken cancellationToken)
    {
        // even the vanilla GetDeposit still tries to obtain the METS ETag
        var getDepositResult = await GetDeposit(request.Id, cancellationToken);
        if (getDepositResult.Success)
        { 
            var wrapperResult = await metsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!, false);
            if (wrapperResult.Success)
            {
                var deposit = getDepositResult.Value!;
                deposit.MetsETag = wrapperResult.Value?.ETag;
                return Result.Ok(deposit);
            }
            return Result.Fail<Deposit>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);
        }
       
        return Result.Fail<Deposit>(getDepositResult.ErrorCode!, getDepositResult.ErrorMessage);
    }
}