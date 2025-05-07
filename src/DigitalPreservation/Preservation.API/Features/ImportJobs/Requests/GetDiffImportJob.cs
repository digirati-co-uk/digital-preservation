using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetDiffImportJob(Deposit deposit, ClaimsPrincipal principal) : IRequest<Result<ImportJob>>
{
    public Deposit Deposit { get; } = deposit;
    public ClaimsPrincipal Principal { get; } = principal;
}

public class GetDiffImportJobHandler(
    WorkspaceManagerFactory workspaceManagerFactory,
    ILogger<GetDiffImportJobHandler> logger,
    IStorageApiClient storageApi,
    ResourceMutator resourceMutator) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        logger.LogInformation("GetDiffImportJob handler called for deposit " + request.Deposit.LogSummary());
        if (request.Deposit.ArchivalGroup == null)
        {
            logger.LogWarning("Deposit " + request.Deposit.Id + "doesn't have Archival Group specified.");
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest,
                "Deposit doesn't have Archival Group specified.");
        }

        if (request.Deposit.Files == null)
        {
            logger.LogWarning("Deposit " + request.Deposit.Id + "doesn't have Deposit location (Files).");
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, "No Deposit location provided.");
        }


        ArchivalGroup? existingArchivalGroup = null;
        var agPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot()!;
        var archivalGroupResult = await storageApi.GetArchivalGroup(request.Deposit.ArchivalGroup.AbsolutePath, null);
        if (archivalGroupResult is { Success: true, Value: not null })
        {
            logger.LogInformation("Archival group is valid");
            existingArchivalGroup = archivalGroupResult.Value;
            resourceMutator.MutateStorageResource(existingArchivalGroup);
        }
        else
        {
            // why did it fail?
            if (archivalGroupResult.ErrorCode == ErrorCodes.NotFound)
            {
                logger.LogInformation("Archival group not found, seeing if path is valid to create one");
                // Is it still a valid path for an archival group - that we can create?
                var testPathResult = await storageApi.TestArchivalGroupPath(agPathUnderRoot);
                if (testPathResult.Failure)
                {
                    logger.LogWarning("Test path returned " + testPathResult.CodeAndMessage());
                    return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, testPathResult.CodeAndMessage());
                }
                logger.LogInformation("Archival group path " + agPathUnderRoot + " would be OK to create at.");
            }
            else
            {
                logger.LogWarning("Archival group invalid for other reasons: " + archivalGroupResult.CodeAndMessage());
                // should this return here?
                return Result.FailNotNull<ImportJob>(archivalGroupResult.ErrorCode!, archivalGroupResult.ErrorMessage);
            }
        }
        
        var workspace = workspaceManagerFactory.Create(request.Deposit);
        var combinedResult = await workspace.GetCombinedDirectory(true);
        if (combinedResult is not { Success: true, Value: not null })
        {
            return Result.FailNotNull<ImportJob>(combinedResult.ErrorCode!, combinedResult.ErrorMessage);
        }
        var combined = combinedResult.Value;
        // We might update this from the METS file later
        var agName = request.Deposit.ArchivalGroupName ?? existingArchivalGroup?.Name ?? combined!.DirectoryInDeposit!.Name;
        if (agName.IsNullOrWhiteSpace() || agName == WorkingDirectory.DefaultRootName)
        {
            // We don't mind no name, but we don't want it to be the default name.
            agName = null;
        }
        logger.LogInformation("(get import source) concluded AG name is " + agName);

        var origin = FolderNames.GetFilesLocation(request.Deposit.Files, workspace.IsBagItLayout);
        var allEncounteredProcessedUris = new List<Uri>();
        var importContainerResult = combined!.ToContainer(request.Deposit.ArchivalGroup, origin, allEncounteredProcessedUris);
        if (importContainerResult.Failure || importContainerResult.Value is null)
        {
            return Result.FailNotNull<ImportJob>(importContainerResult.ErrorCode ?? ErrorCodes.BadRequest, importContainerResult.ErrorMessage);
        }
        var distinctUris = new HashSet<string>();
        var duplicates = allEncounteredProcessedUris.Where(item => !distinctUris.Add(item.ToString())).ToList();
        if (duplicates.Count > 0)
        {
            var message = "Duplicate URIs found after making safe URIs: " + string.Join(", ", duplicates);
            logger.LogWarning(message);
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, message);
        }
        var (sourceContainers, sourceBinaries) = importContainerResult.Value.Flatten();

        var notForImport = $"{agPathUnderRoot}/{IStorage.DepositFileSystem}";
        var removed = sourceBinaries.RemoveAll(b => b.Id.GetPathUnderRoot(true) == notForImport);
        logger.LogInformation("Removed {removed} file matching {notForImport}", removed, notForImport);
 
        var agLocalPathWithSlash = request.Deposit.ArchivalGroup!.LocalPath;
        if (!agLocalPathWithSlash.EndsWith('/'))
        {
            agLocalPathWithSlash += "/";
        }
        foreach (var binary in sourceBinaries)
        {
            var relativeLocalPath = binary.Id!.LocalPath.RemoveStart(agLocalPathWithSlash)!.UnEscapePathElementsNoHashes();
            var combinedFile = combined.FindFile(relativeLocalPath)!; // because it came from a URI not a file path
            if (combinedFile.FileInMets is null)
            {
                var message = $"Could not find file {relativeLocalPath} in METS file.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message);
            }
            if (combinedFile.FileInMets.Digest.IsNullOrWhiteSpace())
            {
                var message = $"File {relativeLocalPath} has no digest in METS file.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message);
            }

            // The incoming METS file should have the correct digest for this file, even if
            // the one in the AG doesn't - in which case it will be identified as a PATCH later
            if (binary.Digest.HasText() && binary.Digest != combinedFile.FileInMets.Digest)
            {
                var message = $"File {relativeLocalPath} has different digest in METS and import source.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Conflict, message);
            }

            binary.Digest = combinedFile.FileInMets.Digest;
            binary.Name = combinedFile.FileInMets.Name;
            if (binary.ContentType.IsNullOrWhiteSpace())
            {
                throw new Exception("We should have set this by now");
            }
            // This was old and wrong:
            // if (combinedFile.FileInMets.ContentType.HasText()) // need to set this
            // {
            //     binary.ContentType = combinedFile.FileInMets.ContentType;
            // }
        }

        var missingTheirChecksum = sourceBinaries
            .Where(b => b.Digest.IsNullOrWhiteSpace())
            .Where(b => b.Id!.GetSlug() != IStorage.DepositFileSystem)
            .ToList();
        if (missingTheirChecksum.Count > 0)
        {
            var first = missingTheirChecksum.First().Id!.GetSlug()?.UnEscapeFromUriNoHashes();
            var message = $"{missingTheirChecksum.Count} file(s) do not have a checksum, including {first}";
            logger.LogWarning(message);
            return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message);
        }
        
        foreach (var container in sourceContainers)
        {
            var relativeLocalPath = container.Id!.LocalPath.RemoveStart(agLocalPathWithSlash)!.UnEscapePathElementsNoHashes();
            var metsDirectory = combined.FindDirectory(relativeLocalPath)?.DirectoryInMets; 
            if (metsDirectory is not null && metsDirectory.Name.HasText())
            {
                container.Name = metsDirectory.Name;
            }
        }

        if (agName == null)
        {
            // No overriding name is being provided
            if (workspace.MetsName.HasText())
            {
                agName = workspace.MetsName;
            }
        }

        var callerIdentity = request.Principal.GetCallerIdentity();
        var now = DateTime.UtcNow;
        var importJob = new ImportJob
        {
            Id = new Uri($"{request.Deposit.Id}/importjobs/transient/{now.Ticks}"),
            ArchivalGroup = request.Deposit.ArchivalGroup,
            Created = now,
            CreatedBy = resourceMutator.GetAgentUri(callerIdentity),
            LastModified = now,
            LastModifiedBy = resourceMutator.GetAgentUri(callerIdentity),
            Source = request.Deposit.Files,
            ArchivalGroupName = agName,
            IsUpdate = existingArchivalGroup != null,
            Deposit = request.Deposit.Id
        };
        
        if (importJob.IsUpdate)
        {
            importJob.SourceVersion = existingArchivalGroup!.Version;
            logger.LogInformation("Import Job is update, will populate Diff tasks.");
            PopulateDiffTasks(combined, existingArchivalGroup, sourceContainers, sourceBinaries, importJob);
        }
        else
        {
            // This is a new object
            importJob.ContainersToAdd = sourceContainers;
            importJob.BinariesToAdd = sourceBinaries;
        }

        var validateMetsResult = workspace.ValidateImportJob(importJob, combined, existingArchivalGroup);
        if (validateMetsResult.Failure)
        {
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, validateMetsResult.ErrorMessage);
        }

        logger.LogInformation("Diff Import Job created: " + importJob.LogSummary());
        return Result.OkNotNull(importJob);
    }
    
    
    private void PopulateDiffTasks(
        CombinedDirectory combined,
        ArchivalGroup archivalGroup, 
        List<Container> sourceContainers,
        List<Binary> sourceBinaries,
        ImportJob importJob)
    {
        var (allExistingContainers, allExistingBinaries) = archivalGroup.Flatten();
        
        var agLocalPathWithSlash =  archivalGroup.Id!.LocalPath;
        if (!agLocalPathWithSlash.EndsWith('/'))
        {
            agLocalPathWithSlash += "/";
        }
        
        importJob.BinariesToAdd.AddRange(sourceBinaries.Where(
            sourceBinary => !allExistingBinaries.Exists(b => b.Id == sourceBinary.Id)));

        foreach (var binary in allExistingBinaries)
        {
            var path = binary.Id!.LocalPath.RemoveStart(agLocalPathWithSlash)!;
            var sourceFile = combined.FindFile(path);
            if (sourceFile is null)
            {
                logger.LogWarning("Binary {path} is in Archival Group but not in deposit files or METS", path);
                importJob.BinariesToDelete.Add(binary);
            }
        }
        
        foreach(var sourceBinary in sourceBinaries.Where(
                    sb => !importJob.BinariesToAdd.Exists(b => b.Id == sb.Id)))
        {
            // files not already put in FilesToAdd
            var existingBinary = allExistingBinaries.Single(eb => eb.Id == sourceBinary.Id);
            if (string.IsNullOrEmpty(existingBinary.Digest) || string.IsNullOrEmpty(sourceBinary.Digest))
            {
                throw new Exception("Missing digest on existing binary in diff operation for " + existingBinary.Id);
            }

            if (existingBinary.Digest != sourceBinary.Digest)
            {
                importJob.BinariesToPatch.Add(sourceBinary);
            }
        }
        
        importJob.BinariesToRename.AddRange(sourceBinaries.Where(BinaryHasDifferentName));

        importJob.ContainersToAdd.AddRange(sourceContainers.Where(
            sc => !allExistingContainers.Exists(existingContainer => existingContainer.Id == sc.Id)));

        importJob.ContainersToDelete.AddRange(allExistingContainers.Where(
            existingContainer => !sourceContainers.Exists(sc => sc.Id == existingContainer.Id)));
        
        importJob.ContainersToRename.AddRange(sourceContainers.Where(ContainerHasDifferentName));

        return;

        bool BinaryHasDifferentName(Binary sourceBinary)
        {
            var existing = allExistingBinaries.SingleOrDefault(b => b.Id == sourceBinary.Id);
            return existing != null && existing.Name != sourceBinary.Name;
        }
        
        bool ContainerHasDifferentName(Container sourceContainer)
        {
            var existing = allExistingContainers.SingleOrDefault(c => c.Id == sourceContainer.Id);
            return existing != null && existing.Name != sourceContainer.Name;
        }
    }
}