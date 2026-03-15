using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;
using DigitalPreservation.XmlGen.Extensions;

namespace DigitalPreservation.Mets;

public class MetadataManager(PremisManager premisManager, PremisManagerExif premisManagerExif, PremisEventManagerVirus premisEventManagerVirus)
{
    private sealed class ProcessingContext
    {
        public required string FileAdmId { get; set; }
        public required string TechId { get; set; }
        public AmdSecType? AmdSec { get; set; }
        public FileType? File { get; set; }
        public MetsTypeFileSecFileGrp? FileGroup { get; set; }
        public XmlElement? PremisIncExifXml { get; set; }
        public XmlElement? VirusXml { get; set; }
    }

    public Result ProcessAllFileMetadata(FullMets fullMets, DivType? div, WorkingFile workingFile, string operationPath, bool newUpload = false)
    {
        var fileId = Constants.FileIdPrefix + operationPath;
        var admId = Constants.AdmIdPrefix + operationPath;
        var techId = Constants.TechIdPrefix + operationPath;

        var ctx = new ProcessingContext { FileAdmId = admId, TechId = techId };

        if (!newUpload)
        {
            ctx.AmdSec = fullMets.Mets.AmdSec.Single(a => a.Id == ctx.FileAdmId);
            var resultGetMetadataXml = GetMetadataXml(ctx, fullMets, div, operationPath);

            if (resultGetMetadataXml.Failure)
                return resultGetMetadataXml;
        }

        var resultProcessFileFormatDataForFile = ProcessFileFormatDataForFile(ctx, workingFile, operationPath, newUpload);

        if (resultProcessFileFormatDataForFile.Failure)
            return resultProcessFileFormatDataForFile;

        if (newUpload)
        {
            ctx.File = new FileType
            {
                Id = fileId,
                Admid = { admId },
                FLocat =
                {
                    new FileTypeFLocat
                    {
                        Href = operationPath, Loctype = FileTypeFLocatLoctype.Url
                    }
                }
            };

            fullMets.Mets.FileSec.FileGrp[0].File.Add(ctx.File);
        }

        if (ctx.File != null)
        {
            var contentTypeFromDeposit = ContentTypes.GetBestContentType(workingFile);
            if (contentTypeFromDeposit.HasText() && contentTypeFromDeposit != ContentTypes.NotIdentified)
            {
                ctx.File.Mimetype = contentTypeFromDeposit;
            }
        }

        ProcessVirusDataForFile(ctx, workingFile);

        if (newUpload)
            fullMets.Mets.AmdSec.Add(ctx.AmdSec);

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

    private Result ProcessFileFormatDataForFile(ProcessingContext ctx, WorkingFile workingFile, string operationPath, bool newUpload)
    {
        FileFormatMetadata premisFile;
        try
        {
            premisFile = GetFileFormatMetadata(workingFile, operationPath);
        }
        catch (MetadataException mex)
        {
            return Result.Fail(ErrorCodes.BadRequest, mex.Message);
        }

        var patchPremisExif = workingFile.GetExifMetadata();

        PremisComplexType? premisType;

        if (ctx.PremisIncExifXml is not null)
        {
            premisType = ctx.PremisIncExifXml.GetPremisComplexType()!;
            premisManager.Patch(premisType, premisFile);
        }
        else
        {
            premisType = premisManager.Create(premisFile);
        }

        if (patchPremisExif is not null)
            premisManagerExif.Patch(premisType, patchPremisExif);

        var premisXml = premisManager.GetXmlElement(premisType, true);

        SetAmdSec(ctx, premisXml, newUpload);

        return Result.Ok();
    }

    private void ProcessVirusDataForFile(ProcessingContext ctx, WorkingFile workingFile)
    {
        var patchPremisVirus = workingFile.GetVirusScanMetadata();

        EventComplexType? virusEventComplexType = null;
        if (ctx.VirusXml is not null)
        {
            virusEventComplexType = ctx.VirusXml.GetEventComplexType()!;

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
        ctx.VirusXml = premisEventManagerVirus.GetXmlElement(virusEventComplexType);

        if (ctx.AmdSec == null) return;

        AddVirusXml(ctx);
    }

    private static Result GetMetadataXml(ProcessingContext ctx, FullMets fullMets, DivType? div, string operationPath)
    {
        if (div != null && div.Type != "Item")
        {
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");
        }

        SetFileAndFileGroup(ctx, div, fullMets);

        if (ctx.File?.FLocat[0].Href != operationPath)
        {
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");
        }

        // TODO: This is a quick fix to get round the problem of spaces in XML IDs.
        // We need to not have any spaces in XML IDs, which means we need to escape them
        // in a reversible way (replacing with _ won't do)
        ctx.FileAdmId = string.Join(' ', ctx.File.Admid);
        var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == ctx.FileAdmId);
        ctx.PremisIncExifXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); //TODO: this includes exif - separate this out
        ctx.VirusXml = amdSec.DigiprovMd.FirstOrDefault(x => x.Id.Contains(Constants.VirusProvEventPrefix))?.MdWrap.XmlData.Any?.FirstOrDefault();

        return Result.Ok();
    }

    private static void SetFileAndFileGroup(ProcessingContext ctx, DivType? div, FullMets fullMets)
    {
        if (div == null) return;
        var fileId = div.Fptr[0].Fileid;
        ctx.FileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        ctx.File = ctx.FileGroup.File.Single(f => f.Id == fileId);
    }

    public AmdSecType GetAmdSecType(FileFormatMetadata premisFile, string admId, string techId, string? digiprovId = null, VirusScanMetadata? virusScanMetadata = null)
    {
        var premis = premisManager.Create(premisFile);
        var xElement = premisManager.GetXmlElement(premis, true);

        var amdSec = new AmdSecType
        {
            Id = admId,
            TechMd =
            {
                new MdSecType
                {
                    Id = techId,
                    MdWrap = new MdSecTypeMdWrap
                    {
                        Mdtype = MdSecTypeMdWrapMdtype.PremisObject,
                        XmlData = new MdSecTypeMdWrapXmlData { Any = { xElement }}
                    }
                }
            },
        };

        if (virusScanMetadata == null) return amdSec;

        var digiProvMd = premisEventManagerVirus.Create(virusScanMetadata);
        var xVirusElement = premisEventManagerVirus.GetXmlElement(digiProvMd);

        amdSec.DigiprovMd.Add(new MdSecType
        {
            Id = digiprovId,
            MdWrap = new MdSecTypeMdWrap
            {
                Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                XmlData = new MdSecTypeMdWrapXmlData { Any = { xVirusElement } }
            }
        });

        return amdSec;
    }

    private static void AddVirusXml(ProcessingContext ctx)
    {
        if (ctx.AmdSec is null)
            return;

        if (ctx.AmdSec.DigiprovMd.Count != 0)
        {
            ctx.AmdSec.DigiprovMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { ctx.VirusXml } };
        }
        else
        {
            ctx.AmdSec.DigiprovMd.Add(new MdSecType
            {
                Id = $"{Constants.VirusProvEventPrefix}{ctx.FileAdmId}",
                MdWrap = new MdSecTypeMdWrap
                {
                    Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                    XmlData = new MdSecTypeMdWrapXmlData { Any = { ctx.VirusXml } }
                }
            });
        }
    }

    private static void SetAmdSec(ProcessingContext ctx, XmlElement? premisXml, bool newUpload)
    {
        if (ctx.AmdSec is null || newUpload)
        {
            ctx.AmdSec = new AmdSecType
            {
                Id = ctx.FileAdmId,
                TechMd =
                {
                    new MdSecType
                    {
                        Id = ctx.TechId,
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
            ctx.AmdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml } };
        }
    }
}
