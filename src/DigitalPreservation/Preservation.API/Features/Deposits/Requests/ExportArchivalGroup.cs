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

public class ExportArchivalGroup(Deposit deposit) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
}

public class ExportArchivalGroupHandler(
    ILogger<ExportArchivalGroupHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage) : IRequestHandler<ExportArchivalGroup, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(ExportArchivalGroup request, CancellationToken cancellationToken)
    {
        // Most of the fields of Deposit will be ignored, including `id`
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

            if (archivalGroupExists is false)
            {
                return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "Archival Group does not exist, cannot export");
            }
            
            var archivalGroupResult = await storageApiClient.GetArchivalGroup(
                request.Deposit.ArchivalGroup.GetPathUnderRoot()!, request.Deposit.VersionExported);
            if (archivalGroupResult.Failure || archivalGroupResult.Value is null)
            {
                return Result.Fail<Deposit?>(archivalGroupResult.ErrorCode!, archivalGroupResult.ErrorMessage);
            }
            var archivalGroup = archivalGroupResult.Value;

            var mintedId = identityService.MintIdentity(nameof(Deposit));
            var callerIdentity = "dlipdev";  // TODO: actual user or app caller identity!
            var filesLocation = await storage.GetWorkingFilesLocation(
                mintedId, useObjectTemplate: false, callerIdentity);
            if (filesLocation.Failure)
            {
                return Result.Fail<Deposit?>(filesLocation.ErrorCode!, filesLocation.ErrorMessage);
            }
            
            var exportResultResult = await storageApiClient.ExportArchivalGroup(
                request.Deposit.ArchivalGroup!,
                filesLocation.Value!,
                archivalGroup.Version!.OcflVersion!);
            if (exportResultResult.Failure || exportResultResult.Value is null)
            {
                return Result.Fail<Deposit?>(exportResultResult.ErrorCode!, exportResultResult.ErrorMessage);
            }
            
            var entity = new DepositEntity
            {
                MintedId = mintedId,
                Created = now,
                CreatedBy = callerIdentity,
                LastModified = now,
                LastModifiedBy = callerIdentity,
                Exported = now,
                ExportedBy = callerIdentity,
                VersionExported = archivalGroup.Version!.OcflVersion,
                ExportResultUri = exportResultResult.Value.Id,
                ArchivalGroupPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot(true),
                Status = DepositStates.Exporting,
                Active = true, 
                SubmissionText = request.Deposit.SubmissionText,
                Files = filesLocation.Value, 
                ArchivalGroupName = archivalGroup.Name
            };
            dbContext.Deposits.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == mintedId);
            var createdDeposit = resourceMutator.MutateDeposit(storedEntity);
            createdDeposit.ArchivalGroupExists = true;
            
            return Result.Ok(createdDeposit);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}