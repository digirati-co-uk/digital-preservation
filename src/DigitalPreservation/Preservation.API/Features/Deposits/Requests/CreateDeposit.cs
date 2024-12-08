using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDeposit(Deposit deposit) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
}

public class CreateDepositHandler(
    ILogger<CreateDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage) : IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (request.Deposit is null)
        {
            return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "No Deposit provided");
        }
        try
        {
            var (archivalGroupExists, validateAgResult) = await ArchivalGroupRequestValidator
                .ValidateArchivalGroup(dbContext, storageApiClient, request.Deposit);
            if (validateAgResult.Failure)
            {
                return validateAgResult;
            }

            var mintedId = identityService.MintIdentity(nameof(Deposit));
            var callerIdentity = "dlipdev";  // TODO: actual user or app caller identity!
            var filesLocation = await storage.GetWorkingFilesLocation(
                mintedId, request.Deposit.UseObjectTemplate ?? false, callerIdentity);
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
                ArchivalGroupPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot(true),
                Status = DepositStates.New,
                // Initially we only become Active if it's FOR an archival group.
                // But changing this to always active for a new Deposit
                Active = true, // archivalGroupPathUnderRoot != null, 
                SubmissionText = request.Deposit.SubmissionText,
                Files = filesLocation.Value, 
                ArchivalGroupName = request.Deposit.ArchivalGroupName
            };
            dbContext.Deposits.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == mintedId);
            var createdDeposit = resourceMutator.MutateDeposit(storedEntity);
            if (archivalGroupExists is true)
            {
                createdDeposit.ArchivalGroupExists = true;
            }
            return Result.Ok(createdDeposit);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}