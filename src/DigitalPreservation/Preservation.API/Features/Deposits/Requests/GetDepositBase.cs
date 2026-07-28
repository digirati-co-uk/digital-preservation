using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDepositBase(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory,
    IMetsParser metsParser)
{
    protected IMetsParser MetsParser { get; } = metsParser;
    protected WorkspaceManagerFactory WorkspaceManagerFactory { get; } = workspaceManagerFactory;

    public async Task<Result<Deposit?>> GetDeposit(string depositId, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == depositId, cancellationToken);
            if (entity != null)
            {
                var wasExportingAndNowFinished = false;
                if (entity.Status == DepositStates.Exporting)
                {
                    // TODO: later can have a background process to update exporting deposits
                    // but for now we'll update on demand

                    if (entity.ExportResultUri is null)
                    {
                        return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Deposit {depositId} is in Exporting state but has no ExportResultUri");
                    }
                    var exportResult = await storageApiClient.GetExport(entity.ExportResultUri);
                    if (exportResult is { Success: true, Value: not null })
                    {
                        if (exportResult.Value.DateFinished.HasValue)
                        {
                            entity.Status = DepositStates.New;
                            await dbContext.SaveChangesAsync(cancellationToken);
                            wasExportingAndNowFinished = true;
                        }
                    }
                    else
                    {
                        return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Could not update exporting status of Deposit {depositId}");
                    }
                }
                
                var deposit = resourceMutator.MutateDeposit(entity);
                var (archivalGroupExists, validateAgResult) = await ArchivalGroupRequestValidator
                    .ValidateArchivalGroup(dbContext, storageApiClient, deposit, null, false);
                if (validateAgResult.Failure)
                {
                    return validateAgResult;
                }

                if (archivalGroupExists is true)
                {
                    deposit.ArchivalGroupExists = true;
                }

                if (wasExportingAndNowFinished)
                {
                    var workspaceManager = await WorkspaceManagerFactory.CreateAsync(deposit, refresh: true);
                    await EnsureMetadataFoldersAfterExport(depositId, deposit, workspaceManager);
                }
                return Result.Ok(deposit);
            }
            return Result.Fail<Deposit?>(ErrorCodes.NotFound, $"Deposit {depositId} not found");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Deposit {depositId} error: {e.Message}");
        }
    }

    // LPII-133: a deposit created against an existing Archival Group with Export=true gets its METS
    // asynchronously, via the export - CreateDepositBase's own "add metadata/ad-hoc folders to METS" step
    // runs before that export completes, so it can't act on a METS that isn't there yet, and
    // CreateDepositBase deliberately no longer scaffolds those folders in S3 for this case either (see the
    // comment there - doing so unconditionally is what caused the folders to end up in S3 but never in
    // METS, the original bug). This reconciles them once the export has actually finished and the deposit's
    // real METS - and its true Editable/Agent status - is known. Only applies where that METS is ours to
    // edit; for a third-party METS there's nothing to reconcile, matching CreateDepositBase's own check.
    private async Task EnsureMetadataFoldersAfterExport(string depositId, Deposit deposit, WorkspaceManager workspaceManager)
    {
        var wrapperResult = await MetsParser.GetMetsFileWrapper(deposit.Files!);
        if (wrapperResult is not { Success: true, Value.Editable: true, Value.PhysicalStructure: not null })
        {
            return;
        }

        var physicalStructure = wrapperResult.Value!.PhysicalStructure!;
        var needsMetadata = physicalStructure.FindDirectory(FolderNames.Metadata) is null;
        var needsAdHoc = physicalStructure.FindDirectory(FolderNames.MetadataAdHoc) is null;
        if (!needsMetadata && !needsAdHoc)
        {
            return;
        }

        // Use the ETag from the METS we just fetched, not whatever the deposit entity happened to carry -
        // CreateFolder writes the S3 folder marker, the deposit file-system cache, and the METS entry
        // together (unlike calling HandleCreateFolder alone, which is METS-only and would reintroduce the
        // same kind of S3/METS mismatch this method exists to fix), and the METS write is ETag-guarded.
        deposit.MetsETag = wrapperResult.Value.ETag;

        // callerIdentity: null is intentional on both calls below - this reconciliation is a system action
        // triggered by an Exporting -> New status transition, not a user-initiated edit, so it should
        // proceed regardless of who (if anyone) currently holds the deposit's edit lock. CreateFolder's
        // lock check (GetOtherLockOwner) only blocks when callerIdentity is non-empty, so passing null
        // deliberately bypasses it.
        if (needsMetadata)
        {
            var result = await workspaceManager.CreateFolder(FolderNames.Metadata, null, false, null, refreshDirectory: true);
            if (result.Failure)
            {
                logger.LogWarning(
                    "Unable to reconcile missing metadata folder for deposit {DepositId} after export: {Message}",
                    depositId, result.CodeAndMessage());
            }
        }

        if (needsMetadata && needsAdHoc)
        {
            // The metadata write above changed the METS file's own S3 ETag - CreateFolder is ETag-guarded
            // (it passes deposit.MetsETag through to GetFullMets as eTagToMatch), so reusing the ETag
            // fetched before that write would make this second call fail its precondition check. Re-fetch
            // just the ETag (parse: false - we already know Editable and which folders are missing, no
            // need to re-parse the whole METS) before the ad-hoc write.
            var refreshedWrapperResult = await MetsParser.GetMetsFileWrapper(deposit.Files!, parse: false);
            if (refreshedWrapperResult is { Success: true, Value: not null })
            {
                deposit.MetsETag = refreshedWrapperResult.Value.ETag;
            }
        }

        if (needsAdHoc)
        {
            var result = await workspaceManager.CreateFolder(FolderNames.AdHoc, FolderNames.Metadata, false, null, refreshDirectory: true);
            if (result.Failure)
            {
                logger.LogWarning(
                    "Unable to reconcile missing metadata/ad-hoc folder for deposit {DepositId} after export: {Message}",
                    depositId, result.CodeAndMessage());
            }
        }
    }
}