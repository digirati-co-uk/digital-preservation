using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetDiffImportJob(Deposit deposit) : IRequest<Result<ImportJob>>
{
    public Deposit Deposit { get; } = deposit;
}

public class GetDiffImportJobHandler(
    IMetsParser metsParser,
    ILogger<GetDiffImportJobHandler> logger,
    IStorageApiClient storageApi,
    ResourceMutator resourceMutator) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        if (request.Deposit.ArchivalGroup == null)
        {
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, "Deposit doesn't have Archival Group specified.");
        }

        var agPathUnderRoot = request.Deposit.ArchivalGroup.GetPathUnderRoot()!;
        
        var importJobResult = await storageApi.GetImportJob(
            agPathUnderRoot,
            request.Deposit.Files!);
        

        if (importJobResult is { Success: true, Value: not null })
        {
            var storageImportJob = importJobResult.Value;
            if (!storageImportJob.IsUpdate && request.Deposit.ArchivalGroupName.HasText())
            {
                storageImportJob.ArchivalGroupName = request.Deposit.ArchivalGroupName;
            }
            storageImportJob.Deposit = request.Deposit.Id;
            

            var notForImport = $"{agPathUnderRoot}/{IStorage.MetsLike}";
            int removed = storageImportJob.BinariesToAdd.RemoveAll(b => b.GetPathUnderRoot() == notForImport);
            logger.LogInformation("Removed {removed} file matching {notForImport}", removed, notForImport);
            
            var sourceBinaries = new List<Binary>();
            sourceBinaries.AddRange(storageImportJob.BinariesToAdd);
            sourceBinaries.AddRange(storageImportJob.BinariesToPatch);
            // embellish...
            var metsWrapperResult = await metsParser.GetMetsFileWrapper(storageImportJob.Source!);
            if (metsWrapperResult.Failure || metsWrapperResult.Value == null)
            {
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,
                    "Could not parse a METS file in the deposit working area.");
            }
            var metsWrapper = metsWrapperResult.Value;
            if (storageImportJob.ArchivalGroupName.IsNullOrWhiteSpace())
            {
                // No overriding name is being provided
                if (metsWrapper.Name.HasText())
                {
                    storageImportJob.ArchivalGroupName = metsWrapper.Name;
                }
            }
            var agString = storageImportJob.ArchivalGroup!.ToString();
            foreach (var binary in sourceBinaries)
            {
                var pathRelativeToArchivalGroup = binary.Id!.ToString().RemoveStart(agString).RemoveStart("/");
                var metsPhysicalFile = metsWrapper.Files.SingleOrDefault(f => f.LocalPath == pathRelativeToArchivalGroup);
                if (metsPhysicalFile is null)
                {
                    return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,
                        $"Could not find file {pathRelativeToArchivalGroup} in METS file.");
                }
                if (metsPhysicalFile.Digest.IsNullOrWhiteSpace())
                {
                    return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,
                        $"File {pathRelativeToArchivalGroup} has no digest in METS file.");
                }
                if (binary.Digest.HasText() && binary.Digest != metsPhysicalFile.Digest)
                {
                    return Result.FailNotNull<ImportJob>(ErrorCodes.Conflict,
                        $"File {pathRelativeToArchivalGroup} has different digest in METS and import source.");
                }
                binary.Digest = metsPhysicalFile.Digest;
                binary.Name = metsPhysicalFile.Name;
                binary.ContentType = metsPhysicalFile.ContentType; // need to set this
            }
            
            var missingTheirChecksum = sourceBinaries
                    .Where(b => b.Digest.IsNullOrWhiteSpace())
                    .Where(b => b.Id!.GetSlug() != IStorage.MetsLike)
                    .ToList();
            if (missingTheirChecksum.Count > 0)
            {
                var first = missingTheirChecksum.First().Id!.GetSlug();
                return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable,
                    $"{missingTheirChecksum.Count} file(s) do not have a checksum, including {first}");
            }
            resourceMutator.MutateStorageImportJob(storageImportJob);
        }
        return importJobResult;
    }
}