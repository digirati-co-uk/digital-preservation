using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDeposit(string id) : IRequest<Result<Deposit?>>
{
    public string Id { get; } = id;
}

public class GetDepositHandler(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<GetDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(GetDeposit request, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
            if (entity != null)
            {
                
                var deposit = resourceMutator.MutateDeposit(entity);
                var (archivalGroupExists, validateAgResult) = await ArchivalGroupRequestValidator
                    .ValidateArchivalGroup(dbContext, storageApiClient, deposit, null, false);
                if (validateAgResult.Failure)
                {
                    return validateAgResult;
                }

                if (archivalGroupExists is true)
                {
                    deposit.ArchivalGroupExists = true;
                }
                return Result.Ok(deposit);
            }
            return Result.Fail<Deposit?>(ErrorCodes.NotFound, $"Deposit {request.Id} not found");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Deposit {request.Id} error: {e.Message}");
        }
    }
}