using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Features.Deposits.Requests;

public class DeleteDepositLock(string id, ClaimsPrincipal user) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string Id { get; } = id;
}

public class DeleteDepositLockHandler(
    ILogger<LockDepositHandler> logger,
    PreservationContext dbContext) : IRequestHandler<DeleteDepositLock, Result>
{
    public async Task<Result> Handle(DeleteDepositLock request, CancellationToken cancellationToken)
    {        
        var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }
        var callerIdentity = request.User.GetCallerIdentity();
        logger.LogInformation("Removing lock on deposit {id} for user {user}", request.Id, callerIdentity);
        // unlocking an already unlocked deposit is a no-op
        entity.LockedBy = null;
        entity.LockDate = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}