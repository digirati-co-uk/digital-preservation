using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Features.Deposits.Requests;

public class LockDeposit(string id, bool force, ClaimsPrincipal user) : IRequest<Result>
{
    public readonly bool Force = force;
    public readonly ClaimsPrincipal User = user;
    public string Id { get; } = id;
}

public class LockDepositHandler(
    ILogger<LockDepositHandler> logger,
    PreservationContext dbContext) : IRequestHandler<LockDeposit, Result>
{
    public async Task<Result> Handle(LockDeposit request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }
        if (entity.LockedBy != null)
        {
            if (!request.Force)
            {
                return Result.Fail(ErrorCodes.Conflict, "Deposit is locked by " + entity.LockedBy);
            }
        }
        var callerIdentity = request.User.GetCallerIdentity();
        logger.LogInformation("Locking deposit {id} for user {user}", request.Id, callerIdentity);
        entity.LockedBy = callerIdentity;
        entity.LockDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}