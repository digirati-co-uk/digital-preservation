using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class PatchDeposit(Deposit deposit, ClaimsPrincipal principal) : IRequest<Result<Deposit>>
{
    public Deposit? Deposit { get; } = deposit;
    public ClaimsPrincipal Principal { get; } = principal;
}

public class PatchDepositHandler(
    ILogger<PatchDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<PatchDeposit, Result<Deposit>>
{
    public async Task<Result<Deposit>> Handle(PatchDeposit request, CancellationToken cancellationToken)
    {
        if (request.Deposit is null)
        {
            return Result.FailNotNull<Deposit>(ErrorCodes.BadRequest, "No Deposit provided");
        }
        var callerIdentity = request.Principal.GetCallerIdentity();
        logger.LogInformation("Patching deposit {id} for user {user}", request.Deposit.Id, callerIdentity);
        try
        {
            
            var mintedId = request.Deposit.Id!.GetSlug();
            
            var (archivalGroupExists, validateAgResult) = await ArchivalGroupRequestValidator
                .ValidateArchivalGroup(dbContext, storageApiClient, request.Deposit, mintedId);
            if (validateAgResult.Failure)
            {
                return Result.FailNotNull<Deposit>(validateAgResult.ErrorCode!, validateAgResult.ErrorMessage);
            }
            
            var entity = await dbContext.Deposits.SingleAsync(
                d => d.MintedId == mintedId, cancellationToken: cancellationToken);

            if (entity.Status == DepositStates.Exporting)
            {
                return Result.FailNotNull<Deposit>(ErrorCodes.Conflict, "Deposit is being exported");
            }
            logger.LogInformation("Deposit.LockedBy is {lockedBy}", entity.LockedBy);
            if (entity.LockedBy is not null && entity.LockedBy != callerIdentity)
            {
                logger.LogWarning("Deposit is locked by {otherLockOwner}, returning Conflict", entity.LockedBy);
                return Result.FailNotNull<Deposit>(ErrorCodes.Conflict, "Deposit is locked by " + entity.LockedBy);
            }
            
            // there are only some patchable fields
            if (request.Deposit.SubmissionText.HasText())
            {
                entity.SubmissionText = request.Deposit.SubmissionText;
            }
            if (request.Deposit.ArchivalGroup != null)
            {
                entity.ArchivalGroupPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot(true);
            }
            if (request.Deposit.ArchivalGroupName.HasText())
            {
                entity.ArchivalGroupName = request.Deposit.ArchivalGroupName;
            }
            
            entity.LastModifiedBy = callerIdentity;
            entity.LastModified = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == entity.MintedId);
            var patchedDeposit = resourceMutator.MutateDeposit(storedEntity);
            if (archivalGroupExists is true)
            {
                patchedDeposit.ArchivalGroupExists = true;
            }
            return Result.OkNotNull(patchedDeposit);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }
}
