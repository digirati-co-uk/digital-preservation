using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDeposit(Deposit deposit, bool export) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
    public bool Export { get; } = export;
}

public class CreateDepositHandler(
    ILogger<CreateDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage,
    IAmazonS3 s3Client,
    IMetsManager metsManager) : IRequestHandler<CreateDeposit, Result<Deposit?>>
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
            
            if(request.Export && archivalGroupExists is false)
            {
                return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "Archival Group does not exist, cannot export");
            }
            
            ArchivalGroup? archivalGroupForExport = null;
            if (request.Export)
            {
                var archivalGroupResult = await storageApiClient.GetArchivalGroup(
                    request.Deposit.ArchivalGroup!.AbsolutePath, request.Deposit.VersionExported);
                if (archivalGroupResult.Failure || archivalGroupResult.Value is null)
                {
                    return Result.Fail<Deposit?>(archivalGroupResult.ErrorCode!, archivalGroupResult.ErrorMessage);
                }
                archivalGroupForExport = archivalGroupResult.Value;
            }
            
            var mintedId = identityService.MintIdentity(nameof(Deposit));
            var callerIdentity = "dlipdev";  // TODO: actual user or app caller identity!
            var filesLocation = await storage.GetWorkingFilesLocation(
                mintedId, request.Deposit.UseObjectTemplate ?? false, callerIdentity);
            if (filesLocation.Failure)
            {
                return Result.Fail<Deposit?>(filesLocation.ErrorCode!, filesLocation.ErrorMessage);
            }

            Uri? exportResultUri = null;
            if (request.Export)
            {
                var exportResultResult = await storageApiClient.ExportArchivalGroup(
                    archivalGroupForExport!.Id!,
                    filesLocation.Value!,
                    archivalGroupForExport!.Version!.OcflVersion!,
                    cancellationToken);
                if (exportResultResult.Failure || exportResultResult.Value is null)
                {
                    return Result.Fail<Deposit?>(exportResultResult.ErrorCode!, exportResultResult.ErrorMessage);
                }
                exportResultUri = exportResultResult.Value.Id;
            }

            var agNameFromDeposit = request.Deposit.ArchivalGroupName ?? archivalGroupForExport?.Name;
            var metsResult = await EnsureMets(
                request.Deposit.UseObjectTemplate is true,
                filesLocation.Value!, 
                request.Deposit.ArchivalGroup,
                archivalGroupExists is true, 
                archivalGroupForExport, 
                request.Deposit.VersionExported,
                agNameFromDeposit,
                cancellationToken);
            
            await storage.GenerateDepositFileSystem( 
                new AmazonS3Uri(filesLocation.Value), true, cancellationToken);

            var entity = new DepositEntity
            {
                MintedId = mintedId,
                Created = now,
                CreatedBy = callerIdentity,
                LastModified = now,
                LastModifiedBy = callerIdentity,
                ArchivalGroupPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot(true),
                // Initially we only become Active if it's FOR an archival group.
                // But changing this to always active for a new Deposit
                Active = true, // archivalGroupPathUnderRoot != null, 
                SubmissionText = request.Deposit.SubmissionText,
                Files = filesLocation.Value,
                Status = request.Export ? DepositStates.Exporting : DepositStates.New,
                ArchivalGroupName = agNameFromDeposit
            };
            if (request.Export)
            {
                entity.Exported = now;
                entity.ExportedBy = callerIdentity;
                entity.VersionExported = archivalGroupForExport!.Version!.OcflVersion;
                entity.ExportResultUri = exportResultUri;
            }

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

    private async Task<Result> EnsureMets(
        bool useObjectTemplate,
        Uri filesLocation, 
        Uri? archivalGroupUri, 
        bool archivalGroupExists, 
        ArchivalGroup? exportedArchivalGroup, 
        string? ocflVersion, 
        string? agNameFromDeposit,
        CancellationToken cancellationToken = default)
    {
        // We need to ensure that there is a METS file in the deposit.
        // It can be a new, empty METS file for a deposit for an AG that doesn't yet exist.
        // If the AG does exist, then we either need to fetch its METS file (for the correct version)
        // or ensure that it is present, if the AG was exported.
        // However _the export will still be queued (or maybe just started)_ at this point.
        // But we can assume that if exportedArchivalGroup contains a METS, then it will be exported.

        if (!archivalGroupExists && useObjectTemplate)
        {
            // the simplest case - but still only for useObjectTemplate=true
            // e.g., Goobi will supply a METS as its next step
            var result = await metsManager.CreateStandardMets(filesLocation, agNameFromDeposit);
            if (result is { Success: true, Value: not null })
            {
                return Result.Ok();
            }
            return Result.Fail(result.ErrorCode!, result.ErrorMessage);
        }

        if (archivalGroupExists)
        {
            ArchivalGroup? archivalGroup;
            // list its root at the correct version
            if (exportedArchivalGroup is null)
            {
                var archivalGroupResult = await storageApiClient.GetArchivalGroup(archivalGroupUri!.AbsolutePath, ocflVersion);
                if (archivalGroupResult is { Success: true, Value: not null, Value.Type: nameof(ArchivalGroup) })
                {
                    archivalGroup = archivalGroupResult.Value;
                }
                else
                {
                    return Result.Fail(
                        archivalGroupResult.ErrorCode ?? ErrorCodes.UnknownError,
                        archivalGroupResult.ErrorMessage ?? "Could not retrieve archival group to look for METS: " + archivalGroupUri);
                }
            }
            else
            {
                archivalGroup = exportedArchivalGroup;
            }

            var metsFile = archivalGroup.Binaries.FirstOrDefault(b => metsManager.IsMetsFile(b.Id!.GetSlug()!));
            if (metsFile is null && useObjectTemplate)
            {
                // existing AG has no METS (even if it's exporting), but we can make one safely because it's one of our own
                var result = await metsManager.CreateStandardMets(filesLocation, archivalGroup, agNameFromDeposit);
                if (result is { Success: true, Value: not null })
                {
                    return Result.Ok();
                }
                return Result.Fail(result.ErrorCode!, result.ErrorMessage);
            }

            if (metsFile is not null && exportedArchivalGroup is null)
            {
                // There is a METS file, but this isn't an export, so we need to copy it into the Deposit
                // TODO: refactor this into IStorage interface
                var source = new AmazonS3Uri(metsFile.Origin);
                var dest = new AmazonS3Uri(filesLocation + metsFile.GetSlug());
                var req = new CopyObjectRequest
                {
                    SourceBucket = source.Bucket,
                    SourceKey = source.Key,
                    DestinationBucket = dest.Bucket,
                    DestinationKey = dest.Key,
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var resp = await s3Client.CopyObjectAsync(req, cancellationToken);
                var hexChecksum = AwsChecksum.FromBase64ToHex(resp.ChecksumSHA256);
                if(resp is { ChecksumSHA256: not null } && hexChecksum == metsFile.Digest)
                {
                    return Result.Ok();
                }
                return Result.Fail(ErrorCodes.Conflict, $"Exported METS file checksum {metsFile.Digest} doesn't match AWS calculated checksum {hexChecksum}");
            }
        }
        // No action
        return Result.Ok();
    }
}