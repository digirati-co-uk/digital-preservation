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
                if (entity.Status == DepositStates.Exporting)
                {
                    // TODO: later can have a background process to update exporting deposits
                    // but for now we'll update on demand

                    if (entity.ExportResultUri is null)
                    {
                        return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Deposit {request.Id} is in Exporting state but has no ExportResultUri");
                    }
                    var exportResult = await storageApiClient.GetExport(entity.ExportResultUri);
                    if (exportResult is { Success: true, Value: not null })
                    {
                        if (exportResult.Value.DateFinished.HasValue)
                        {
                            entity.Status = DepositStates.New;
                            await dbContext.SaveChangesAsync(cancellationToken);
                        }
                    }
                    else
                    {
                        return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Could not update exporting status of Deposit {request.Id}");
                    }
                }
                
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