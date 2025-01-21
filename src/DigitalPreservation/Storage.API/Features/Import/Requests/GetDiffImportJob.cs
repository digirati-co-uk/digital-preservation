using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;

namespace Storage.API.Features.Import.Requests;

// TODO: I think this IRequest is used by storage and preservation?
// embellish from METS is something only preservation API should know about but we don't want to make 2 import jobs..

// No - Storage API reads everything from the source but does not embellish from METS.
// This class is storage API only.
// But Preservation API does the embellishing.
public class GetDiffImportJob(
    ArchivalGroup? archivalGroup, 
    Uri sourceUri, 
    string archivalGroupPathUnderRoot, 
    string? archivalGroupName, 
    bool errorIfMissingChecksum, 
    bool relyOnMetsLike) : IRequest<Result<ImportJob>>
{
    public ArchivalGroup? ArchivalGroup { get; } = archivalGroup;
    public Uri SourceUri { get; } = sourceUri;
    public string ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
    public string? ArchivalGroupName { get; } = archivalGroupName;
    public bool ErrorIfMissingChecksum { get; } = errorIfMissingChecksum;
    public bool RelyOnMetsLike { get; } = relyOnMetsLike;
}

public class GetDiffImportJobHandler(IStorage storage, Converters converters) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        var importSourceResult = await storage.GetImportSource(
            request.SourceUri, 
            request.RelyOnMetsLike,
            cancellationToken);
        if (importSourceResult.Failure)
        {
            return Result.FailNotNull<ImportJob>(importSourceResult.ErrorCode!, importSourceResult.ErrorMessage);
        }
        var source = importSourceResult.Value!;
        var agName = request.ArchivalGroupName ?? request.ArchivalGroup?.Name ?? source.Name;
        if (agName.IsNullOrWhiteSpace() || agName == WorkingDirectory.DefaultRootName)
        {
            // We don't mind no name, but we don't want it to be the default name.
            agName = null;
        }
        var callerIdentity = "dlipdev";
        var now = DateTime.UtcNow;
        var importJob = new ImportJob
        {
            Id = converters.GetTransientResourceId("diff"),
            ArchivalGroup = converters.RepositoryUriFromPathUnderRoot(request.ArchivalGroupPathUnderRoot),
            Created = now,
            CreatedBy = converters.GetAgentUri(callerIdentity),
            LastModified = now,
            LastModifiedBy = converters.GetAgentUri(callerIdentity),
            Source = request.SourceUri,
            ArchivalGroupName = agName
        };     
        
        var importContainer = source.AsContainer(importJob.ArchivalGroup);
        var (sourceContainers, sourceBinaries) = importContainer.Flatten();

        if (request.ErrorIfMissingChecksum)
        {
            var missingTheirChecksum = sourceBinaries
                .Where(b => b.Digest.IsNullOrWhiteSpace())
                .Where(b => b.Id!.GetSlug() != IStorage.MetsLike)
                .ToList();
            if (missingTheirChecksum.Count > 0)
            {
                var first = missingTheirChecksum.First().Id!.GetSlug();
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, $"{missingTheirChecksum.Count} file(s) do not have a checksum, including {first}");
            }
        }
        if (request.ArchivalGroup == null)
        {
            // This is a new object
            importJob.ContainersToAdd = sourceContainers;
            importJob.BinariesToAdd = sourceBinaries;
        }
        else
        {
            importJob.ArchivalGroupName = request.ArchivalGroup.Name;
            importJob.IsUpdate = true;
            importJob.SourceVersion = request.ArchivalGroup.Version;
            PopulateDiffTasks(request.ArchivalGroup, sourceContainers, sourceBinaries, importJob);
        }
        
        return Result.OkNotNull(importJob);
    }

    private void PopulateDiffTasks(ArchivalGroup archivalGroup, 
        List<Container> sourceContainers, List<Binary> sourceBinaries,
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
                throw new Exception("Missing digest in diff operation for " + existingBinary.Id);
            }

            if (existingBinary.Digest != sourceBinary.Digest)
            {
                importJob.BinariesToPatch.Add(sourceBinary);
            }
        }

        importJob.ContainersToAdd.AddRange(sourceContainers.Where(
            sc => !allExistingContainers.Exists(existingContainer => existingContainer.Id == sc.Id)));

        importJob.ContainersToDelete.AddRange(allExistingContainers.Where(
            existingContainer => !sourceContainers.Exists(sc => sc.Id == existingContainer.Id)));
    }
    
}