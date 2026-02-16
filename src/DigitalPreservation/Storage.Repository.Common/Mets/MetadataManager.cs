using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;
using DigitalPreservation.XmlGen.Extensions;

namespace Storage.Repository.Common.Mets;
public class MetadataManager(IPremisManager<FileFormatMetadata> premisManager, IPremisManager<ExifMetadata> premisManagerExif, IPremisEventManager<VirusScanMetadata> premisEventManagerVirus) : IMetadataManager
{
    public Result ProcessAllFileMetadata(ref FullMets fullMets, DivType? div, WorkingFile workingFile, string operationPath, bool newUpload = false)
    {
        var identifiers = FilenameHelpers.GetIdSafeOperationPath(operationPath);

        TechId = identifiers.TechId;
        FileAdmId = identifiers.AdmId;
        PremisIncExifXml = null;
        VirusXml = null;

        if (!newUpload)
        {
            AmdSec = fullMets.Mets.AmdSec.Single(a => a.Id == FileAdmId);
            var resultGetMetadataXml = GetMetadataXml(ref fullMets, div, operationPath);

            if (resultGetMetadataXml.Failure)
                return resultGetMetadataXml;
        }

        var resultProcessFileFormatDataForFile = ProcessFileFormatDataForFile(workingFile, operationPath, newUpload);

        if(resultProcessFileFormatDataForFile.Failure)
            return resultProcessFileFormatDataForFile;

        if (newUpload)
        {
            File = new FileType
            {
                Id = identifiers.FileId,
                Admid = { identifiers.AdmId },
                FLocat =
                {
                    new FileTypeFLocat
                    {
                        Href = operationPath, Loctype = FileTypeFLocatLoctype.Url
                    }
                }
            };

            fullMets.Mets.FileSec.FileGrp[0].File.Add(File);
        }

        if (File != null)
        {
            var contentTypeFromDeposit = ContentTypes.GetBestContentType(workingFile);
            if (contentTypeFromDeposit.HasText() && contentTypeFromDeposit != ContentTypes.NotIdentified)
            {
                File.Mimetype = contentTypeFromDeposit;
            }
        }

        ProcessVirusDataForFile(workingFile);

        if (newUpload)
            fullMets.Mets.AmdSec.Add(AmdSec);

        AmdSec = null;

        return Result.Ok();
    }

    private static FileFormatMetadata GetFileFormatMetadata(WorkingFile workingFile, string originalName)
    {
        // This will throw if mismatches
        var digestMetadata = workingFile.GetDigestMetadata();

        var fileFormatMetadata = workingFile.GetFileFormatMetadata();
        if (fileFormatMetadata != null)
        {
            if (fileFormatMetadata.OriginalName.IsNullOrWhiteSpace())
            {
                fileFormatMetadata.OriginalName = originalName;
            }

            return fileFormatMetadata;
        }

        // no metadata available
        return new FileFormatMetadata
        {
            Source = Constants.Mets,
            ContentType = workingFile.ContentType,
            Digest = digestMetadata?.Digest ?? workingFile.Digest,
            Size = workingFile.Size,
            OriginalName = originalName, // workingFile.LocalPath
            StorageLocation = null // storageLocation
        };
    }

    private Result ProcessFileFormatDataForFile(WorkingFile workingFile, string operationPath, bool newUpload)
    {
        try
        {
            PremisFile = GetFileFormatMetadata(workingFile, operationPath);
        }
        catch (MetadataException mex)
        {
            return Result.Fail(ErrorCodes.BadRequest, mex.Message);
        }

        var patchPremisExif = workingFile.GetExifMetadata();

        PremisComplexType? premisType;

        if (PremisIncExifXml is not null)
        {
            premisType = PremisIncExifXml.GetPremisComplexType()!;
            premisManager.Patch(premisType, PremisFile);
        }
        else
        {
            premisType = premisManager.Create(PremisFile);
        }

        if (patchPremisExif is not null) 
            premisManagerExif.Patch(premisType, patchPremisExif);

        var premisXml = premisManager.GetXmlElement(premisType, true);

        SetAmdSec(premisXml, newUpload);

        return Result.Ok();
    }

    private void ProcessVirusDataForFile(WorkingFile workingFile)
    {
        var patchPremisVirus = workingFile.GetVirusScanMetadata();

        EventComplexType? virusEventComplexType = null;
        if (VirusXml is not null)
        {
            virusEventComplexType = VirusXml.GetEventComplexType()!;

            if (patchPremisVirus != null)
            {
                premisEventManagerVirus.Patch(virusEventComplexType, patchPremisVirus);
            }
        }
        else
        {
            if (patchPremisVirus != null)
            {
                virusEventComplexType = premisEventManagerVirus.Create(patchPremisVirus);
            }
        }

        if (virusEventComplexType is null) return;
        VirusXml = premisEventManagerVirus.GetXmlElement(virusEventComplexType);

        if (AmdSec == null) return;

        AddVirusXml();
    }

    private Result GetMetadataXml(ref FullMets fullMets, DivType? div, string operationPath)
    {
        if (div != null && div.Type != "Item")
        {
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");
        }

        SetFileAndFileGroup(div, fullMets);

        if (File?.FLocat[0].Href != operationPath)
        {
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");
        }

        // TODO: This is a quick fix to get round the problem of spaces in XML IDs.
        // We need to not have any spaces in XML IDs, which means we need to escape them 
        // in a reversible way (replacing with _ won't do)
        FileAdmId = string.Join(' ', File.Admid);
        var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == FileAdmId);
        //AmdSec = amdSec;
        var premisIncExifXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); //TODO: this includes exif - separate this out
        var virusPremisXml = amdSec.DigiprovMd.FirstOrDefault(x => x.Id.Contains(Constants.VirusProvEventPrefix))?.MdWrap.XmlData.Any?.FirstOrDefault();

        PremisIncExifXml = premisIncExifXml;
        VirusXml = virusPremisXml;

        return Result.Ok();
    }

    private void SetFileAndFileGroup(DivType? div, FullMets fullMets)
    {
        if (div == null) return;
        var fileId = div.Fptr[0].Fileid;
        FileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        File = FileGroup.File.Single(f => f.Id == fileId);
    }

    private void AddVirusXml()
    {
        if (AmdSec is null)
            return;

        if (AmdSec.DigiprovMd.Any())
        {
            AmdSec.DigiprovMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { VirusXml } };
        }
        else
        {
            AmdSec.DigiprovMd.Add(new MdSecType
            {
                Id = $"{Constants.VirusProvEventPrefix}{FileAdmId}",
                MdWrap = new MdSecTypeMdWrap
                {
                    Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                    XmlData = new MdSecTypeMdWrapXmlData { Any = { VirusXml } }
                }
            });
        }
    }

    private void SetAmdSec(XmlElement? premisXml, bool newUpload)
    {
        if (AmdSec is null || newUpload)
        {
            AmdSec = new AmdSecType
            {
                Id = FileAdmId, //admId
                TechMd =
                {
                    new MdSecType
                    {
                        Id = TechId, //techId
                        MdWrap = new MdSecTypeMdWrap
                        {
                            Mdtype = MdSecTypeMdWrapMdtype.PremisObject,
                            XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml }}
                        }
                    }
                },
            };
        }
        else
        {
            AmdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml } };
        }
    }

    private XmlElement? PremisIncExifXml { get; set; }
    private XmlElement? VirusXml { get; set; }
    private XmlElement? ExifXml { get; set; } //TODO: to follow when Exif has its own premis manager

    private string? FileAdmId { get; set; }
    private AmdSecType? AmdSec { get; set; } //TODO: may not be a good idea??
    private FileType? File { get; set; }
    private string? TechId { get; set; }
    private FileFormatMetadata? PremisFile { get; set; }
    private MetsTypeFileSecFileGrp? FileGroup { get; set; }
}
