using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Storage.Repository.Common;

namespace Preservation.API.Features.Deposits.Requests;

public class DeleteDeposit(string id) : IRequest<Result>
{
    public string Id { get; } = id;
}

public class DeleteDepositHandler(
    ILogger<DeleteDepositHandler> logger,
    PreservationContext dbContext,
    IStorage storage) : IRequestHandler<DeleteDeposit, Result>
{
    public async Task<Result> Handle(DeleteDeposit request, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
            if (entity != null)
            {
                var storageLocation = entity.Files;
                dbContext.Deposits.Remove(entity);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (storageLocation != null)
                {
                    var deleteFilesResult = await storage.EmptyStorageLocation(storageLocation, cancellationToken);
                    if (deleteFilesResult.Success)
                    {
                        return Result.Ok();
                    }

                    return deleteFilesResult;
                }
            }
            return Result.Fail(ErrorCodes.NotFound, $"Deposit {request.Id} not found");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, $"Deposit {request.Id} deletion error: {e.Message}");
        }
    }
}