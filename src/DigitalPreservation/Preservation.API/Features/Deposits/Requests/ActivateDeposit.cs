using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Features.Deposits.Requests;

public class ActivateDeposit(string id, bool active, ClaimsPrincipal user ) : IRequest<Result>
{
    public string Id { get; } = id;
    public bool Active { get; } = active;
    public ClaimsPrincipal User { get; } = user;
}


public class ActivateDepositHandler : IRequestHandler<ActivateDeposit, Result>
{
    private readonly ILogger<ActivateDepositHandler> _logger;
    private readonly PreservationContext _dbContext;
    public ActivateDepositHandler(ILogger<ActivateDepositHandler> logger, PreservationContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    public async Task<Result> Handle(ActivateDeposit request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }
        //guard
        if (!request.Active && entity.Status == "new")
        {
            return Result.Fail(ErrorCodes.NotFound, "deposit.active = false not allowed on deposit.status == new" + request.Id);
        }
        
        var callerIdentity = request.User.GetCallerIdentity();
        _logger.LogInformation("Setting active state of deposit {id} to {active} for user {user}", request.Id, request.Active, callerIdentity);
        entity.Active = request.Active;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}