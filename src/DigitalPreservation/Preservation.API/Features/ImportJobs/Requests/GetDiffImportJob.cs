using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetDiffImportJob(Deposit deposit) : IRequest<Result<ImportJob>>
{
    public Deposit Deposit { get; } = deposit;
}

public class GetDiffImportJobHandler(
    WorkspaceManagerFactory workspaceManagerFactory,
    ILogger<GetDiffImportJobHandler> logger,
    IStorage storage,
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

        var importSourceResult = await storage.GetImportSource(
            request.Deposit.Files,
            cancellationToken);
        if (importSourceResult.Failure)
        {
            return Result.FailNotNull<ImportJob>(importSourceResult.ErrorCode!, importSourceResult.ErrorMessage);
        }

        var source = importSourceResult.Value!;
        // We might update this from the METS file later
        var agName = request.Deposit.ArchivalGroupName ?? existingArchivalGroup?.Name ?? source.Name;
        if (agName.IsNullOrWhiteSpace() || agName == WorkingDirectory.DefaultRootName)
        {
            // We don't mind no name, but we don't want it to be the default name.
            agName = null;
        }
        logger.LogInformation("(get import source) concluded AG name is " + agName);

        // Now embellish the source
        var importContainer = source.AsContainer(request.Deposit.ArchivalGroup);
        var (sourceContainers, sourceBinaries) = importContainer.Flatten();

        var notForImport = $"{agPathUnderRoot}/{IStorage.DepositFileSystem}";
        var removed = sourceBinaries.RemoveAll(b => b.GetPathUnderRoot() == notForImport);
        logger.LogInformation("Removed {removed} file matching {notForImport}", removed, notForImport);
        
        logger.LogInformation("(get import source) embellishing from METS...");
        var workspace = workspaceManagerFactory.Create(request.Deposit);
        var metsWrapperResult = await metsParser.GetMetsFileWrapper(request.Deposit.Files);
        if (metsWrapperResult.Failure || metsWrapperResult.Value == null)
        {
            logger.LogError("Could not parse a METS file in the deposit working area. " + metsWrapperResult.CodeAndMessage());
            return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,
                "Could not parse a METS file in the deposit working area.");
        }

        var metsWrapper = metsWrapperResult.Value;

        // The following could be refactored for use on the deposit page
        // for now just do it here to understand it and embellish the JOB files in the source
        // This combines the two sources of info - METS and
        // For the Deposit page we want to hold them sparate I think

        var agString = request.Deposit.ArchivalGroup.ToString();
        foreach (var binary in sourceBinaries)
        {
            var pathRelativeToArchivalGroup = binary.Id!.ToString().RemoveStart(agString).RemoveStart("/");
            var metsPhysicalFile = metsWrapper.Files.SingleOrDefault(f => f.LocalPath == pathRelativeToArchivalGroup);
            if (metsPhysicalFile is null)
            {
                var message = $"Could not find file {pathRelativeToArchivalGroup} in METS file.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,message);
            }

            if (metsPhysicalFile.Digest.IsNullOrWhiteSpace())
            {
                var message = $"File {pathRelativeToArchivalGroup} has no digest in METS file.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message);
            }

            if (binary.Digest.HasText() && binary.Digest != metsPhysicalFile.Digest)
            {
                var message = $"File {pathRelativeToArchivalGroup} has different digest in METS and import source.";
                logger.LogWarning(message);
                return Result.FailNotNull<ImportJob>(ErrorCodes.Conflict, message);
            }

            binary.Digest = metsPhysicalFile.Digest;
            binary.Name = metsPhysicalFile.Name;
            if (metsPhysicalFile.ContentType.HasText()) // need to set this
            {
                binary.ContentType = metsPhysicalFile.ContentType;
            }
        }

        var missingTheirChecksum = sourceBinaries
            .Where(b => b.Digest.IsNullOrWhiteSpace())
            .Where(b => b.Id!.GetSlug() != IStorage.DepositFileSystem)
            .ToList();
        if (missingTheirChecksum.Count > 0)
        {
            var first = missingTheirChecksum.First().Id!.GetSlug();
            var message = $"{missingTheirChecksum.Count} file(s) do not have a checksum, including {first}";
            logger.LogWarning(message);
            return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message);
        }

        foreach (var container in sourceContainers)
        {
            var pathRelativeToArchivalGroup = container.Id!.ToString().RemoveStart(agString).RemoveStart("/");
            var metsDirectory = metsWrapper.PhysicalStructure?.FindDirectory(pathRelativeToArchivalGroup);
            if (metsDirectory is not null && metsDirectory.Name.HasText())
            {
                container.Name = metsDirectory.Name;
            }
        }

        if (agName == null)
        {
            // No overriding name is being provided
            if (metsWrapper.Name.HasText())
            {
                agName = metsWrapper.Name;
            }
        }



        var callerIdentity = "dlipdev";
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
            PopulateDiffTasks(existingArchivalGroup, sourceContainers, sourceBinaries, importJob);
        }
        else
        {
            // This is a new object
            importJob.ContainersToAdd = sourceContainers;
            importJob.BinariesToAdd = sourceBinaries;
        }

        logger.LogInformation("Diff Import Job created: " + importJob.LogSummary());
        return Result.OkNotNull(importJob);
    }
    
    
    private void PopulateDiffTasks(
        ArchivalGroup archivalGroup, 
        List<Container> sourceContainers,
        List<Binary> sourceBinaries,
        ImportJob importJob)
    {
        var (allExistingContainers, allExistingBinaries) = archivalGroup.Flatten();
        
        importJob.BinariesToAdd.AddRange(sourceBinaries.Where(
            sourceBinary => !allExistingBinaries.Exists(b => b.Id == sourceBinary.Id)));
        
        importJob.BinariesToDelete.AddRange(allExistingBinaries.Where(
            existingBinary => !sourceBinaries.Exists(b => b.Id == existingBinary.Id)));
        
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