using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Deposits.Requests;

public class PatchDeposit(Deposit deposit) : IRequest<Result<Deposit>>
{
    public Deposit? Deposit { get; } = deposit;
}

public class PatchDepositHandler(
    ILogger<PatchDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<PatchDeposit, Result<Deposit>>
{
    public async Task<Result<Deposit>> Handle(PatchDeposit request, CancellationToken cancellationToken)
    {
        if (request.Deposit is null)
        {
            return Result.FailNotNull<Deposit>(ErrorCodes.BadRequest, "No Deposit provided");
        }
        try
        {
            var entity = await dbContext.Deposits.SingleAsync(
                d => d.MintedId == request.Deposit.Id!.GetSlug(), cancellationToken: cancellationToken);
            
            // there are only some patchable fields
            // come back and see what others are patchable as we go
            entity.SubmissionText = request.Deposit.SubmissionText;
            entity.ArchivalGroupPathUnderRoot = request.Deposit.ArchivalGroup?.GetPathUnderRoot();
            entity.ArchivalGroupName = request.Deposit.ArchivalGroupName;
            
            var callerIdentity = "dlipdev";  // TODO: actual user or app caller identity!
            entity.LastModifiedBy = callerIdentity;
            entity.LastModified = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == entity.MintedId);
            var createdDeposit = resourceMutator.MutateDeposit(storedEntity);
            return Result.OkNotNull(createdDeposit);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }
}
