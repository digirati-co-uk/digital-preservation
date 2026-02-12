using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Features.Deposits.Requests;

public class ActiveDeposit(string id, bool active, ClaimsPrincipal user ) : IRequest<Result>
{
    public string Id { get; } = id;
    public bool Active { get; } = active;
    public ClaimsPrincipal User { get; } = user;
}


public class ActiveDepositHandler : IRequestHandler<ActiveDeposit, Result>
{
    private readonly ILogger<ActiveDepositHandler> _logger;
    private readonly PreservationContext _dbContext;
    public ActiveDepositHandler(ILogger<ActiveDepositHandler> logger, PreservationContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    public async Task<Result> Handle(ActiveDeposit request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }
        var callerIdentity = request.User.GetCallerIdentity();
        _logger.LogInformation("Setting active state of deposit {id} to {active} for user {user}", request.Id, request.Active, callerIdentity);
        entity.Active = request.Active;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}