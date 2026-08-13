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

        /// <summary>
        /// ID for a virus-scan digiprovMD this run may need to create. Derived from the file's
        /// PATH, not from <see cref="FileAdmId"/>: on a pre-issue-#188 document that resolves to
        /// the legacy raw amdSec ID, and embedding it would mint a brand-new ID containing a
        /// slash and a space - after step 2, from the code step 2 was supposed to have made
        /// schema-valid. Nothing anywhere references a digiprovMD by ID (the readers find it
        /// structurally, or by prefix), so there is no compatibility cost to choosing our own.
        /// </summary>
        public required string DigiprovId { get; set; }
        public AmdSecType? AmdSec { get; set; }
        public FileType? File { get; set; }
        public MetsTypeFileSecFileGrp? FileGroup { get; set; }
        public XmlElement? PremisIncExifXml { get; set; }
        public XmlElement? VirusXml { get; set; }
    }

    public Result ProcessAllFileMetadata(FullMets fullMets, DivType? div, WorkingFile workingFile, string operationPath, bool newUpload = false)
    {
        var idPart = operationPath.ToMetsId();
        var fileId = Constants.FileIdPrefix + idPart;
        var admId = Constants.AdmIdPrefix + idPart;
        var techId = Constants.TechIdPrefix + idPart;

        var ctx = new ProcessingContext
        {
            FileAdmId = admId,
            TechId = techId,
            DigiprovId = $"{Constants.VirusProvEventPrefix}{Constants.AdmIdPrefix}{idPart}"
        };

        if (!newUpload)
        {
            // GetMetadataXml resolves the file's amdSec from its ADMID tokens and sets
            // ctx.AmdSec / ctx.FileAdmId from the resolved element - which handles legacy
            // space-containing IDs, whose form may not equal the ID minted above in a
            // mixed-format METS.
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
            premisManagerExif.Patch(premisType, patchPremisExif, operationPath);

        var patchExtent = workingFile.GetExtentMetadata();
        if (patchExtent is not null)
            PremisManagerExif.PatchExtent(premisType, patchExtent, operationPath);

        var premisXml = PremisManager.GetXmlElement(premisType, true);

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
        ctx.VirusXml = PremisEventManagerVirus.GetXmlElement(virusEventComplexType);

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

        if (ctx.File == null || MetsCache.NormalisePathKey(ctx.File.FLocat[0].Href) != operationPath)
        {
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");
        }

        // ADMID is IDREFS: legacy platform IDs containing spaces arrive split into several
        // tokens, while a schema-valid METS may genuinely reference several amdSecs.
        // IdRefs handles both; FileAdmId is then the RESOLVED amdSec's actual ID (identical
        // to the rejoined tokens for legacy content), so IDs derived from it - the ClamAV
        // digiprovMD ID - always embed a real amdSec ID.
        var amdSec = IdRefs.ResolveSingle(ctx.File.Admid, id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id));
        if (amdSec == null)
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"No amdSec found for ADMID '{IdRefs.Joined(ctx.File.Admid)}' of file {operationPath}");
        }
        ctx.FileAdmId = amdSec.Id;
        ctx.AmdSec = amdSec;
        ctx.PremisIncExifXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); //TODO: this includes exif - separate this out
        ctx.VirusXml = amdSec.DigiprovMd.FirstOrDefault(x => x.Id.Contains(Constants.VirusProvEventPrefix))?.MdWrap.XmlData.Any?.FirstOrDefault();

        return Result.Ok();
    }

    private static void SetFileAndFileGroup(ProcessingContext ctx, DivType? div, FullMets fullMets)
    {
        if (div == null) return;
        var fileId = div.Fptr[0].Fileid;
        ctx.FileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == Constants.ObjectsFileGrpUse);
        ctx.File = ctx.FileGroup.File.Single(f => f.Id == fileId);
    }

    public AmdSecType GetAmdSecType(FileFormatMetadata premisFile, string admId, string techId, string? digiprovId = null, VirusScanMetadata? virusScanMetadata = null)
    {
        var premis = premisManager.Create(premisFile);
        var xElement = PremisManager.GetXmlElement(premis, true);

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
        var xVirusElement = PremisEventManagerVirus.GetXmlElement(digiProvMd);

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
                Id = ctx.DigiprovId,
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
