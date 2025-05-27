using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Storage.Repository.Common;

namespace Preservation.API.Features.Deposits.Requests;

public class DeleteDeposit(string id, ClaimsPrincipal user) : IRequest<Result>
{
    public string Id { get; } = id;
    public readonly ClaimsPrincipal User = user;
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
            var callerIdentity = request.User.GetCallerIdentity();
            var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
            if (entity != null)
            {
                if (entity.LockedBy != null && entity.LockedBy != callerIdentity)
                {
                    return Result.Fail(ErrorCodes.Conflict, "Deposit is locked by " + entity.LockedBy);
                }
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