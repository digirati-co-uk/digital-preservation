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
public class MetsFromArchivalGroup(IMetsManager metsManager, IMetsParser metsParser, MetadataManager metadataManager)
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

        var fullMets = new FullMets { Mets = mets, Uri = file };
        // This FullMets is write-only (navigation happens on a fresh load, which populates the
        // cache), so the cache build inside AssertNavigable is debug/test-only: it puts this
        // construction path under the same navigability check as MetsManager mutations - a
        // structMap this class builds that cannot be resolved by path is a bug. In Release the
        // whole call compiles out and no cache walk happens.
        AssertNavigable(fullMets);

        var writeResult = await metsManager.WriteMets(fullMets);
        if (writeResult.Success)
        {
            return await metsParser.GetMetsFileWrapper(file);
        }
        return Result.FailNotNull<MetsFileWrapper>(writeResult.ErrorCode!, writeResult.ErrorMessage);
    }
    
    /// <summary>
    /// Debug-build check (explicit throw rather than Debug.Assert, which can take down the
    /// whole test process in .NET test hosts) that the structMap this class built is fully
    /// navigable by path.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private static void AssertNavigable(FullMets fullMets)
    {
        var pathDiagnostics = MetsCache.Populate(fullMets);
        if (pathDiagnostics.Count > 0)
        {
            throw new InvalidOperationException(
                "MetsFromArchivalGroup produced a structMap that is not fully navigable by path: "
                + string.Join("; ", pathDiagnostics));
        }
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
            var localPath = childContainer.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");

            // The template already contains divs (and amdSecs) for objects/, metadata/ and
            // metadata/ad-hoc/ - reuse any div the parent already has for this path rather
            // than adding a duplicate div and duplicate-ID amdSec (which previously happened
            // for any Archival Group with a preserved metadata/ folder). Matching is by the
            // div's resolved path (premis:originalName), not by reconstructing its ID, so it
            // stays correct when ID minting changes (issue #188 step 2).
            var childDirectoryDiv = div.Div.FirstOrDefault(
                d => MetsCache.TryResolvePath(d, mets) == localPath);

            if (childDirectoryDiv == null)
            {
                var idPart = localPath.ToMetsId();
                var admId = Constants.AdmIdPrefix + idPart;
                var techId = Constants.TechIdPrefix + idPart;
                childDirectoryDiv = new DivType
                {
                    Type = Constants.DirectoryType,
                    Label = childContainer.Name,
                    Id = $"{Constants.PhysIdPrefix}{idPart}",
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
            var idPart = localPath.ToMetsId();
            var fileId = Constants.FileIdPrefix + idPart;
            var admId = Constants.AdmIdPrefix + idPart;
            var techId = Constants.TechIdPrefix + idPart;
            var childItemDiv = new DivType
            {
                Type = Constants.ItemType,
                Label = binary.Name,
                Id = $"{Constants.PhysIdPrefix}{idPart}",
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