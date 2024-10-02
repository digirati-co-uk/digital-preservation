using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Repository.Common;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDeposit(Deposit deposit) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
}

public class CreateDepositHandler(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorage storage) : IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        string? archivalGroupPathUnderRoot = null;
        if (request.Deposit is null)
        {
            return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "No Deposit provided");
        }

        try
        {
            if (request.Deposit.ArchivalGroup != null)
            {
                // validate - we do this in UI but this is an API call
                archivalGroupPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot();
                if (dbContext.Deposits.Any(d => d.Active && d.ArchivalGroupPathUnderRoot == archivalGroupPathUnderRoot))
                {
                    return Result.Fail<Deposit?>(ErrorCodes.Conflict,
                        "An Active Deposit already exists for this archivalGroup (" + archivalGroupPathUnderRoot + ")");
                }
            }

            var mintedId = identityService.MintIdentity(nameof(Deposit));
            // it only becomes Active if it's FOR an archival group; look for that in Update
            var callerIdentity = "dlipdev";  // TODO: actual user or app caller identity!
            var filesLocation = await storage.GetWorkingFilesLocation(mintedId, callerIdentity);
            if (filesLocation.Failure)
            {
                return Result.Fail<Deposit?>(filesLocation.ErrorCode!, filesLocation.ErrorMessage);
            }
            var entity = new DepositEntity
            {
                MintedId = mintedId,
                Created = now,
                CreatedBy = callerIdentity,
                LastModified = now,
                LastModifiedBy = callerIdentity,
                ArchivalGroupPathUnderRoot = archivalGroupPathUnderRoot,
                Status = "new",
                Active = archivalGroupPathUnderRoot != null, // see note above
                SubmissionText = request.Deposit.SubmissionText,
                Files = filesLocation.Value, 
                ArchivalGroupName = request.Deposit.ArchivalGroupName
            };
            dbContext.Deposits.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == mintedId);
            var createdDeposit = resourceMutator.MutateDeposit(storedEntity);
            return Result.Ok(createdDeposit);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}