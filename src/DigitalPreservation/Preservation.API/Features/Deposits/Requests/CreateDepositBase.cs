using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using LeedsDlipServices.Identity;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDepositBase(
    ILogger<CreateDepositBase> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage,
    IMetsManager metsManager,
    MetsFromArchivalGroup metsFromArchivalGroup,
    WorkspaceManagerFactory workspaceManagerFactory,
    IMetsParser metsParser,
    IClientDirectory clientDirectory)
{
    protected async Task<Result<Deposit?>> HandleBase(CreateDeposit request, CancellationToken cancellationToken)
    {        
        var now = DateTime.UtcNow;
        if (request.Deposit is null)
        {
            logger.LogWarning("No Deposit provided to CreateDepositHandler");
            return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "No Deposit provided");
        }
        try
        {
            logger.LogInformation("Preservation API Create Deposit called: {DepositSummary}", request.Deposit.LogSummary());
            var (archivalGroupExists, validateAgResult) = await ArchivalGroupRequestValidator
                .ValidateArchivalGroup(dbContext, storageApiClient, request.Deposit);
            if (validateAgResult.Failure)
            {
                logger.LogWarning("Validation failed for Deposit provided to CreateDepositHandler, {CodeAndMessage}", validateAgResult.CodeAndMessage());
                return validateAgResult;
            }
            
            if(request.Export && archivalGroupExists is false)
            {
                logger.LogWarning("Archival Group does not exist, cannot export");
                return Result.Fail<Deposit?>(ErrorCodes.BadRequest, "Archival Group does not exist, cannot export");
            }
            
            // Will be a storage map not an AG
            StorageMap? storageMapForExport = null;
            string? nameOfArchivalGroupAtVersion = null;
            if (request.Export)
            {
                var agPath = request.Deposit.ArchivalGroup!.GetPathUnderRoot()!;
                var agVersion = request.Deposit.VersionExported;
                logger.LogInformation("CreateDeposit request asked for Export, " +
                                      "fetching storage map for Archival Group {AgUrl}",
                                        request.Deposit.ArchivalGroup!.AbsolutePath);
                var storageMapResult = await storageApiClient.GetStorageMap(agPath, agVersion);
                if (storageMapResult.Failure || storageMapResult.Value is null)
                {
                    logger.LogError(storageMapResult.CodeAndMessage());
                    return Result.Fail<Deposit?>(storageMapResult.ErrorCode!, storageMapResult.ErrorMessage);
                }
                storageMapForExport = storageMapResult.Value;
                var nameResult = await storageApiClient.GetArchivalGroupName(agPath, agVersion);
                if (nameResult.Success)
                {
                    nameOfArchivalGroupAtVersion = nameResult.Value;
                }
                logger.LogInformation("Storage map for Archival Group retrieved: {Version}", storageMapForExport.Version);
            }
            
            var mintedId = identityService.MintIdentity(nameof(Deposit));
            var callerIdentity = request.Principal.GetCallerIdentity();
            logger.LogInformation("Identity service gave us deposit Id: {MintedId}", mintedId);

            // For a deposit against an existing Archival Group, the AG's own (exported) content
            // defines the deposit's content - including whether it has metadata/ad-hoc folders.
            // Scaffolding them here unconditionally caused a bug (LPII-133): for an export, the AG's
            // METS arrives asynchronously and isn't present yet, so the matching "add these folders to
            // METS" step below silently skips (see the Editable check), leaving the folders present in
            // S3 but absent from METS. GetDepositBase reconciles this once the export has finished and
            // the real METS is available (see the wasExportingAndNowFinished branch there).
            var createMetadataFolders = archivalGroupExists is not true;

            // Carve the deposit's working files area in the caller's own bucket when their
            // (token-resolved) profile has one; null means the storage default (RFC-0001 §8).
            // This is the only place the choice is made - from here on, deposit.Files carries
            // the full location and nothing downstream consults the default.
            var workingRoot = CallerResolver.ResolveDepositBucket(request.Principal, clientDirectory);
            var filesLocation = await storage.GetWorkingFilesLocation(
                mintedId, request.Deposit.Template, workingRoot, createMetadataFolders);
            if (filesLocation.Failure)
            {
                logger.LogError("Unable to create GetWorkingFilesLocation for deposit {MintedId}; {CodeAndMessage}", mintedId, filesLocation.CodeAndMessage());
                return Result.Fail<Deposit?>(filesLocation.ErrorCode!, filesLocation.ErrorMessage);
            }

            Uri? exportResultUri = null;
            if (request.Export)
            {
                logger.LogInformation("CreateDeposit request asked for Export, " +
                                      "calling storage::ExportArchivalGroup to export {AgUri}, version: {Version}",
                    request.Deposit.ArchivalGroup, storageMapForExport!.Version.OcflVersion!);
                var exportResultResult = await storageApiClient.ExportArchivalGroup(
                    resourceMutator.MutatePreservationApiUri(request.Deposit.ArchivalGroup!)!, // maybe should ask for the AG and use its URL
                    filesLocation.Value!,
                    storageMapForExport.Version.OcflVersion!,
                    cancellationToken);
                if (exportResultResult.Failure || exportResultResult.Value is null)
                {
                    logger.LogError("Failed to export {ArchivalGroup}, {CodeAndMessage}", request.Deposit.ArchivalGroup!, exportResultResult.CodeAndMessage());
                    return Result.Fail<Deposit?>(exportResultResult.ErrorCode!, exportResultResult.ErrorMessage);
                }
                exportResultUri = exportResultResult.Value.Id;
                logger.LogInformation("Obtained exportResultUri: {ExportResultUri}", exportResultUri!);
            }

            var agNameFromDeposit = request.Deposit.ArchivalGroupName ?? nameOfArchivalGroupAtVersion;
            
            var metsResult = await EnsureMets(
                request.Deposit.Template,
                filesLocation.Value!, 
                request.Deposit.ArchivalGroup,
                archivalGroupExists is true, 
                storageMapForExport,
                request.Deposit.VersionExported,
                agNameFromDeposit,
                cancellationToken);

            if (metsResult.Failure)
            {
                logger.LogError("Unable to ensure METS file in deposit: {CodeAndMessage}", metsResult.CodeAndMessage());
                return Result.Fail<Deposit?>(metsResult.ErrorCode!, metsResult.ErrorMessage);
            }
            logger.LogInformation("Result from EnsureMets is success {Success}", metsResult.Success);

            var metadataReader = await MetadataReader.Create(storage, filesLocation.Value!);
            await storage.GenerateDepositFileSystem(filesLocation.Value!, true, metadataReader.Decorate, cancellationToken);

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
                entity.VersionExported = storageMapForExport!.Version.OcflVersion;
                entity.ExportResultUri = exportResultUri;
            }
            
            logger.LogInformation("Saving deposit entity to dbContext");

            dbContext.Deposits.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Now recover:
            var storedEntity = dbContext.Deposits.Single(d => d.MintedId == mintedId);
            var createdDeposit = resourceMutator.MutateDeposit(storedEntity);
            if (archivalGroupExists is true)
            {
                createdDeposit.ArchivalGroupExists = true;
            }

            var wrapperResult = await metsParser.GetMetsFileWrapper(createdDeposit.Files!);

            // Only ensure the metadata/ad-hoc folders when there is a METS file we created and can edit.
            // For template=None or third-party METS there is nothing to write to, and HandleCreateFolder
            // would fail internally (GetFullMets returns NotFound / BadRequest). For an export whose METS
            // hasn't arrived yet, wrapperResult.Value will be non-editable (or absent) here regardless of
            // createMetadataFolders - that's fine, since createMetadataFolders is now false for exports and
            // GetDepositBase performs the equivalent reconciliation once the real METS has arrived.
            if (wrapperResult.Value is { Editable: true } metsWrapper)
            {
                if (metsWrapper.PhysicalStructure!.FindDirectory(FolderNames.Metadata) == null)
                    await CreateFolderInMets(FolderNames.Metadata, FolderNames.Metadata, createdDeposit);

                if (metsWrapper.PhysicalStructure.FindDirectory(FolderNames.MetadataAdHoc) == null)
                    await CreateFolderInMets(FolderNames.MetadataAdHoc, FolderNames.AdHoc, createdDeposit);
            }

            if (!request.Export)
            {
                // refresh the file system
                // The async export will do this at the end of its run, if we exported.
                // But here we didn't, so just do a quick update (it won't take long)
                await workspaceManagerFactory.CreateAsync(createdDeposit, true);
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
        TemplateType templateType,
        Uri filesLocation,
        Uri? archivalGroupUri,
        bool archivalGroupExists,
        StorageMap? storageMapForExport,
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

        logger.LogInformation("Ensuring a METS file in {FilesLocation}; template={TemplateType}", filesLocation, templateType);
        if (!archivalGroupExists && templateType != TemplateType.None)
        {
            // the simplest case - but still only for templated Deposits
            // e.g., Goobi will supply a METS as its next step
            logger.LogInformation(
                "Archival Group does not yet exist, and template={TemplateType}, so create a standard METS file",
                templateType);
            // Standard or BagIt layout?
            var metsLocation = FolderNames.GetFilesLocation(filesLocation, templateType == TemplateType.BagIt);
            var result = await metsManager.CreateStandardMets(metsLocation, agNameFromDeposit);
            if (result is { Success: true, Value: not null })
            {
                return Result.Ok();
            }

            logger.LogError("Unable to create standard METS: {CodeAndMessage}", result.CodeAndMessage());
            return Result.Fail(result.ErrorCode!, result.ErrorMessage);
        }

        if (archivalGroupExists)
        {
            logger.LogInformation("Archival Group {ArchivalGroupUri} exists.", archivalGroupUri);
            StorageMap? storageMap;
            // list its root at the correct version
            if (storageMapForExport is null)
            {
                logger.LogInformation("Not already retrieved, so fetch archival group {ArchivalGroupUri}, version {OcflVersion}",
                    archivalGroupUri, ocflVersion);
                var storageMapResult =
                    await storageApiClient.GetStorageMap(archivalGroupUri!.GetPathUnderRoot()!, ocflVersion);
                if (storageMapResult is { Success: true, Value: not null })
                {
                    logger.LogInformation("Storage Map retrieved: {OcflVersion}", storageMapResult.Value.Version.OcflVersion);
                    storageMap = storageMapResult.Value;
                }
                else
                {
                    logger.LogError("Unable to fetch Storage Map: {CodeAndMessage}", storageMapResult.CodeAndMessage());
                    return Result.Fail(
                        storageMapResult.ErrorCode ?? ErrorCodes.UnknownError,
                        storageMapResult.ErrorMessage ??
                        "Could not retrieve storage map to look for METS: " + archivalGroupUri);
                }
            }
            else
            {
                storageMap = storageMapForExport;
            }

            // An exported Archival Group is, for now, always in our root-level template, not in BagIt format


            var metsFile = storageMap.Files.Values.FirstOrDefault(f => MetsUtils.IsMetsFile(f.FullPath))?.FullPath;
            if (metsFile is null)
            {
                logger.LogWarning("No METS file found in Archival Group {ArchivalGroupUri}, version {OcflVersion}",
                    archivalGroupUri, ocflVersion);
            }

            if (metsFile is null && templateType != TemplateType.None)
            {
                logger.LogWarning("Creating Standard METS file in AG {ArchivalGroupUri}, version {OcflVersion}", archivalGroupUri, ocflVersion);
                // existing AG has no METS (even if it's exporting), but we can make one safely because it's one of our own
                // FOR NOW THIS THE EXPORT IS ALWAYS ROOT LEVEL, MATCHING THE AG
                // We will only do this (add a METS file where none existed) if you are exporting the HEAD
                // Otherwise what would it mean to add METS to an older version?
                if (storageMap.Version.OcflVersion != storageMap.HeadVersion.OcflVersion)
                {
                    return Result.Fail(ErrorCodes.UnknownError,
                        "If exporting an Archival Group that doesn't have a METS, you can only export the HEAD version.");
                }

                var archivalGroupResult = await storageApiClient.GetArchivalGroup(archivalGroupUri!.AbsolutePath);
                if (archivalGroupResult is { Success: true, Value: not null })
                {
                    var result = await metsFromArchivalGroup.CreateStandardMets(filesLocation,
                        archivalGroupResult.Value,
                        agNameFromDeposit);
                    if (result is { Success: true, Value: not null })
                    {
                        return Result.Ok();
                    }

                    logger.LogError("Unable to create Standard METS file, {CodeAndMessage}", result.CodeAndMessage());
                    return Result.Fail(result.ErrorCode!, result.ErrorMessage);
                }

                return Result.Fail(ErrorCodes.UnknownError,
                    "Could not load Archival Group {}.");
            }

            if (metsFile is not null && storageMapForExport is null)
            {
                logger.LogInformation(
                    "There is a METS file, but this isn't an export, so we need to copy it into the Deposit.");
                var storageArchivalGroupUri = resourceMutator.MutatePreservationApiUri(archivalGroupUri);
                // For now this will always export to the root
                var exportMetsResult = await storageApiClient.ExportArchivalGroupMetsOnly(storageArchivalGroupUri!,
                    filesLocation, ocflVersion, cancellationToken);
                if (exportMetsResult is { Success: true, Value: not null, Value.DateFinished: not null })
                {
                    return Result.Ok();
                }

                logger.LogError("Unable to copy METS file to deposit: {CodeAndMessage}", exportMetsResult.CodeAndMessage());
                return Result.Fail(exportMetsResult.ErrorCode!, exportMetsResult.ErrorMessage);
            }
        }

        // No action
        logger.LogInformation("Ensure METS concludes no METS file should be created.");

        return Result.Ok();
    }

    private async Task CreateFolderInMets(string pathName, string folderName, Deposit deposit)
    {
        var dir = new WorkingDirectory
        {
            LocalPath = FolderNames.GetPathPrefix(deposit.Template == TemplateType.BagIt) + pathName,
            Name = folderName,
            Modified = DateTime.UtcNow
        };

        var dirForMets = deposit.Template == TemplateType.BagIt ? dir.ToRootLayout() : dir;

        await metsManager.HandleCreateFolder(deposit.Files!, dirForMets, deposit.MetsETag!);
    }

}