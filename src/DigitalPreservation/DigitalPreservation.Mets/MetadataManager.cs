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
        var virusScan = workingFile.GetVirusScanMetadata();
        if (virusScan == null)
        {
            // Nothing was scanned in this upload, so there is no event to record. Whatever
            // provenance the amdSec already holds - earlier scans, or events from tools we
            // know nothing about - is left exactly as it is.
            return;
        }

        ctx.VirusXml = PremisEventManagerVirus.GetXmlElement(premisEventManagerVirus.Create(virusScan));

        if (ctx.AmdSec == null) return;

        // A scan is an event, and one event is recorded once. Reading the same unchanged ClamAV
        // output a second time - which happens whenever this file is put through the METS writer
        // again while the last run's output is still sitting in the deposit - is not a second scan
        // (issue #221). Two scans of the same file cannot share an instant, so the scan's own time
        // is what tells them apart; a genuine re-scan writes a later one and is appended normally.
        if (AlreadyRecorded(ctx, virusScan))
        {
            return;
        }

        AppendVirusEvent(ctx);
    }

    /// <summary>
    /// Whether this exact scan is already in the amdSec, matched on the event's date. Only events we
    /// recognise as virus checks are considered; anything else present - earlier scans, provenance
    /// from tools we know nothing about - is neither read for meaning nor disturbed.
    /// </summary>
    private static bool AlreadyRecorded(ProcessingContext ctx, VirusScanMetadata virusScan)
    {
        if (virusScan.Timestamp == default)
        {
            // Metadata from a source that does not say when it scanned. Nothing here can tell that
            // apart from a re-scan, and a duplicated event is a much smaller problem than a scan
            // that went unrecorded, so record it.
            return false;
        }

        var scannedAt = EventDateTime(ctx.VirusXml);
        if (scannedAt is null)
        {
            return false;
        }

        return ctx.AmdSec!.DigiprovMd
            .Select(digiprovMd => digiprovMd.MdWrap?.XmlData?.Any.FirstOrDefault())
            .OfType<XmlElement>()
            .Where(IsVirusCheck)
            .Any(existing => EventDateTime(existing) == scannedAt);
    }

    private static bool IsVirusCheck(XmlElement premisEvent) =>
        ChildValue(premisEvent, "eventType") == Constants.VirusCheckEventType;

    private static string? EventDateTime(XmlElement? premisEvent) =>
        premisEvent is null ? null : ChildValue(premisEvent, "eventDateTime");

    private static string? ChildValue(XmlElement premisEvent, string localName)
    {
        var elements = premisEvent.GetElementsByTagName(localName, Constants.PremisNamespace);
        return elements.Count == 0 ? null : elements[0]?.InnerText.Trim();
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
        // The candidate must actually carry technical metadata. ADMID is a LIST and its order
        // means nothing, so a file may name a shared rightsMD before the section holding its
        // own techMD - the Archivematica shape. Accepting the first section that merely EXISTS
        // would hand back the rights section, and the update would then write this file's
        // PREMIS into it (or throw indexing TechMd[0]) - issue #215.
        var amdSec = IdRefs.ResolveSingle(
            ctx.File.Admid,
            id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id),
            CarriesPremisObject);
        if (amdSec == null || !CarriesPremisObject(amdSec))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"No amdSec carrying PREMIS technical metadata found for ADMID " +
                $"'{IdRefs.Joined(ctx.File.Admid)}' of file {operationPath}");
        }
        ctx.FileAdmId = amdSec.Id;
        ctx.AmdSec = amdSec;
        ctx.PremisIncExifXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); //TODO: this includes exif - separate this out
        // The existing digiprovMDs are deliberately NOT read here. A scan is appended as a new
        // event rather than merged into an old one, so nothing about what is already recorded
        // needs interpreting - which is also what lets provenance we don't recognise pass
        // through untouched.

        return Result.Ok();
    }

    /// <summary>
    /// True when this amdSec carries the file's PREMIS object — the thing the metadata paths
    /// actually read and patch. Merely having SOME techMD is not enough: an Archivematica-shaped
    /// ADMID can name a section whose techMD holds only FITS or EXIF output, and treating that
    /// as usable stops resolution on the wrong section, then hands non-PREMIS XML to
    /// <c>GetPremisComplexType()</c> to deserialise into a null it does not expect. This is the
    /// same check the parser and the path cache use, so all three agree on what "usable" means.
    /// </summary>
    private static bool CarriesPremisObject(AmdSecType amdSec) =>
        amdSec.TechMd.Any(techMd =>
            techMd.MdWrap?.XmlData?.Any?.FirstOrDefault() is XmlElement element &&
            (element.LocalName == "object" && element.NamespaceURI == Constants.PremisNamespace ||
             element.GetElementsByTagName("object", Constants.PremisNamespace).Count > 0));

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

    /// <summary>
    /// Record this scan as a NEW digiprovMD. A digiprovMD holds a digital provenance EVENT, and
    /// events accumulate: each scan is its own event, with its own outcome and date, and the
    /// file's current virus status is simply the most recent of them. Nothing already present
    /// is modified or removed — not earlier scans, and not events written by tools we neither
    /// recognise nor need to understand (issue #219).
    /// </summary>
    private static void AppendVirusEvent(ProcessingContext ctx)
    {
        if (ctx.AmdSec is null)
            return;

        ctx.AmdSec.DigiprovMd.Add(new MdSecType
        {
            Id = UnusedDigiprovId(ctx),
            MdWrap = new MdSecTypeMdWrap
            {
                Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                XmlData = new MdSecTypeMdWrapXmlData { Any = { ctx.VirusXml } }
            }
        });
    }

    /// <summary>
    /// A digiprovMD ID for this scan that nothing in the amdSec is already using. Events
    /// accumulate, so the first scan takes the plain ID and each later one is suffixed; an
    /// xs:ID has to stay unique within the document. IDs we did not mint are counted as taken
    /// without being interpreted.
    /// </summary>
    private static string UnusedDigiprovId(ProcessingContext ctx)
    {
        // Only this file's own amdSec needs checking, because the ID embeds this file's admId
        // and so cannot collide with another file's. Scanning the whole document would make
        // minting O(total scans in the deposit) for every file that gets an event.
        var taken = ctx.AmdSec!.DigiprovMd
            .Select(digiprovMd => digiprovMd.Id)
            .Where(id => id != null)
            .ToHashSet(StringComparer.Ordinal);

        if (!taken.Contains(ctx.DigiprovId))
        {
            return ctx.DigiprovId;
        }

        // The counter goes BEFORE the admId, not after it. A trailing "_2" would read as part
        // of a path: "…ClamAV_ADM_objects_x002F_a_2" is both the second scan of "objects/a" and
        // the plain conventional ID of a file genuinely named "objects/a_2", so a reader that
        // resolves by ID could report one file's virus status as another's. A conventional ID
        // always has the admId's own prefix straight after the ClamAV prefix, never a digit, so
        // putting the counter there cannot be confused with any file's key.
        var identifier = ctx.DigiprovId[Constants.VirusProvEventPrefix.Length..];
        var occurrence = 2;
        while (taken.Contains(Constants.NumberedVirusProvEventId(occurrence, identifier)))
        {
            occurrence++;
        }
        return Constants.NumberedVirusProvEventId(occurrence, identifier);
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
        else if (ctx.AmdSec.TechMd.Count > 0)
        {
            ctx.AmdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml } };
        }
        else
        {
            // Resolution only accepts an amdSec that already has a techMD, so this is
            // unreachable today - but it used to throw here rather than degrade, and an
            // unhandled index is a 500 on the upload path where everything else is a
            // Result.Fail. Writing the techMD we were about to update is the repair.
            ctx.AmdSec.TechMd.Add(new MdSecType
            {
                Id = ctx.TechId,
                MdWrap = new MdSecTypeMdWrap
                {
                    Mdtype = MdSecTypeMdWrapMdtype.PremisObject,
                    XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml } }
                }
            });
        }
    }
}
