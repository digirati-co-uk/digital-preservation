using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;

namespace Storage.Repository.Common.Mets;

/// <summary>
/// Companion class to MetsManager. The difference is that it knows what an Archival Group is,
/// and can create METS files from an existing Archival Group, if it doesn't already have one.
/// </summary>
/// <param name="metsManager"></param>
/// <param name="metsParser"></param>
public class MetsFromArchivalGroup(MetsManager metsManager, MetsParser metsParser, MetadataManager metadataManager) : IMetsFromArchivalGroup
{
    /// <summary>
    /// Reverse-engineer a METS file from an existing Archival Group.
    /// </summary>
    /// <param name="metsLocation"></param>
    /// <param name="archivalGroup"></param>
    /// <param name="agNameFromDeposit"></param>
    /// <returns></returns>
    public async Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit)
    {
        var (file, mets) = await metsManager.GetStandardMets(metsLocation, agNameFromDeposit);
        
        AddResourceToMets(mets, archivalGroup.Id!, mets.StructMap[0].Div, archivalGroup);
        
        var writeResult = await metsManager.WriteMets(new FullMets{ Mets = mets, Uri = file });
        if (writeResult.Success)
        {
            return await metsParser.GetMetsFileWrapper(file);
        }
        return Result.FailNotNull<MetsFileWrapper>(writeResult.ErrorCode!, writeResult.ErrorMessage);
    }
    
    /// <summary>
    /// This builds up the METS file from repository resources, not working files
    /// </summary>
    /// <param name="mets"></param>
    /// <param name="archivalGroupUri"></param>
    /// <param name="div"></param>
    /// <param name="container"></param>
    private void AddResourceToMets(DigitalPreservation.XmlGen.Mets.Mets mets, Uri archivalGroupUri, DivType div, Container container)
    {
        var agLocalPath = archivalGroupUri.LocalPath;
        foreach (var childContainer in container.Containers)
        {
            DivType? childDirectoryDiv = null;
            if (container is ArchivalGroup && childContainer.GetSlug() == FolderNames.Objects)
            {
                // The objects div should already exist from our template
                childDirectoryDiv = mets.StructMap[0].Div.Div.Single(d => d.Id == Constants.ObjectsDivId);
            }

            if (childDirectoryDiv == null)
            {
                var localPath = childContainer.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");
                var admId = Constants.AdmIdPrefix + localPath;
                var techId = Constants.TechIdPrefix + localPath;
                childDirectoryDiv = new DivType
                {
                    Type = Constants.DirectoryType,
                    Label = childContainer.Name,
                    Id = $"{Constants.PhysIdPrefix}{localPath}",
                    Admid = { admId }
                };
                div.Div.Add(childDirectoryDiv);
                var reducedPremisForObjectDir = new FileFormatMetadata
                {
                    Source = Constants.Mets,
                    OriginalName = localPath,
                    StorageLocation = childContainer.Id
                };
                mets.AmdSec.Add(metadataManager.GetAmdSecType(reducedPremisForObjectDir, admId, techId));
            }

            AddResourceToMets(mets, archivalGroupUri, childDirectoryDiv, childContainer);
        }

        AddBinariesToMets(container.Binaries, agLocalPath, div, mets);
    }

    
    private void AddBinariesToMets(List<Binary> binaries, string agLocalPath, DivType div, DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        foreach (var binary in binaries)
        {
            var localPath = binary.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");
            if (MetsUtils.IsMetsFile(localPath!, true))
            {
                continue;
            }
            var fileId = Constants.FileIdPrefix + localPath;
            var admId = Constants.AdmIdPrefix + localPath;
            var techId = Constants.TechIdPrefix + localPath;
            var childItemDiv = new DivType
            {
                Type = Constants.ItemType,
                Label = binary.Name,
                Id = $"{Constants.PhysIdPrefix}{localPath}",
                Fptr = { new DivTypeFptr { Fileid = fileId } }
            };
            div.Div.Add(childItemDiv);
            mets.FileSec.FileGrp[0].File.Add(
                new FileType
                {
                    Id = fileId,
                    Admid = { admId },
                    Mimetype = binary.ContentType,
                    FLocat = {
                        new FileTypeFLocat
                        {
                            Href = localPath, Loctype = FileTypeFLocatLoctype.Url
                        }
                    }
                });
            var premisFile = new FileFormatMetadata
            {
                Source = Constants.Mets,
                Digest = binary.Digest,
                Size = binary.Size,
                OriginalName = localPath,
                StorageLocation = binary.Id
            };
            mets.AmdSec.Add(metadataManager.GetAmdSecType(premisFile, admId, techId));
        }
    }
}