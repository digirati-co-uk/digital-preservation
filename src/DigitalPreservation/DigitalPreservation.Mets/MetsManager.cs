using System.Diagnostics;
using System.Xml;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets.AccessConditions;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.Mets;

/// <param name="options">
/// Optional, and null in the tests that construct this directly - which is what keeps the default
/// behaviour exactly what it was. Dependency injection always supplies it.
/// </param>
public class MetsManager(
    IMetsParser metsParser,
    IMetsStorage metsStorage,
    MetadataManager metadataManager,
    IOptions<MetsManagerOptions>? options = null,
    ILogger<MetsManager>? logger = null,
    IDerivedAccessConditionResolver? derivedAccessConditionResolver = null) : IMetsManager
{
    private bool NormaliseOnWrite => options?.Value.NormaliseMetsIdsOnWrite ?? false;

    public async Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit)
    {
        var (file, mets) = await GetStandardMets(metsLocation, agNameFromDeposit);
        var writeResult = await WriteMets(new FullMets{ Mets = mets, Uri = file });
        if (writeResult.Success)
        {
            return await metsParser.GetMetsFileWrapper(file);
        }
        return Result.FailNotNull<MetsFileWrapper>(writeResult.ErrorCode!, writeResult.ErrorMessage);
    }

    public async Task<(Uri file, DigitalPreservation.XmlGen.Mets.Mets mets)> GetStandardMets(Uri metsLocation, string? agNameFromDeposit)
    {
        // might be a file path or an S3 URI
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (root, file) = fileLocResult.Value;
        if (file is null)
        {
            file = new Uri(root + "mets.xml");
        }

        var mets = GetEmptyMets();
        var mods = ModsManager.CreateRootMods(agNameFromDeposit ?? "[Untitled]");
        var physRoot = mets.StructMap[0].Div;
        ModsManager.SetModsForDiv(mets, physRoot, mods);
        return (file, mets);
    }


    /// <summary>
    /// Write the METS, first migrating its IDs if <see cref="MetsManagerOptions.NormaliseMetsIdsOnWrite"/>
    /// is set.
    /// </summary>
    /// <remarks>
    /// This is the single point every mutation passes through - HandleSingleChange, AddItemsToMets,
    /// DeleteItems, SetLogicalStructMap, RemoveLogicalStructMap and SetModsInformation all end here
    /// - which is why the migration hangs off it rather than off each of them. Building a fresh
    /// document (CreateStandardMets, MetsFromArchivalGroup) also comes through, and normalising one
    /// of those is a no-op, because nothing minted since issue #214 is invalid.
    /// </remarks>
    public async Task<Result> WriteMets(FullMets fullMets)
    {
        // Every write recomputes the derived access conditions for the whole document first (issue
        // #433), so a change to an ancestor's chosen values can never leave a stale derived value on
        // a descendant. A no-op where nothing is configured to be derived - but a provider that
        // EXPECTS a profile and cannot get one fails the write, since writing without recomputing
        // would leave the old derived values beside the new chosen ones.
        try
        {
            await ApplyDerivedAccessConditions(fullMets);
        }
        catch (AccessConditionProfileUnavailableException e)
        {
            logger?.LogError(e, "Refusing to write {MetsUri}: the access condition profile is unavailable, so its derived access conditions cannot be recomputed", fullMets.Uri);
            return Result.Fail(ErrorCodes.UpstreamUnavailable,
                "The access condition profile could not be fetched, so the METS file was not written: " + e.Message);
        }

        if (NormaliseOnWrite)
        {
            var normalisation = NormaliseIds(fullMets);
            if (normalisation is { Success: false, ErrorCode: ErrorCodes.Unprocessable })
            {
                // The duplicate-ID refusal, which happens before anything is touched - so the
                // document in memory is exactly what the caller built, and writing it changes
                // nothing about its (pre-existing) invalidity. Refusing here instead would make
                // every edit to the deposit fail until the service-wide flag was turned off for
                // everyone: a duplicate ID is a per-document problem (issue #216 can mint one on a
                // failed-and-retried add), and it should not take a whole deployment's writes with
                // it. So write, unnormalised, and say so loudly - the migration campaign and the
                // logs are the nets that bring a person to it.
                logger?.LogError(
                    "Writing {MetsUri} WITHOUT normalising its IDs: {ErrorMessage}",
                    fullMets.Uri, normalisation.ErrorMessage);
                return await metsStorage.WriteMets(fullMets);
            }
            if (normalisation is not { Success: true, Value: not null })
            {
                // Any other failure means the rewrite left the document unnavigable by path -
                // which it should not be able to do, since paths are not touched. The in-memory
                // document has already been rewritten, so writing it would persist something we
                // have just decided we do not understand. Refuse instead - the ETag is untouched,
                // so the caller can retry, and the flag can be turned off.
                logger?.LogError(
                    "Refusing to write {MetsUri}: normalising its IDs failed - {ErrorMessage}",
                    fullMets.Uri, normalisation.ErrorMessage);
                return Result.Fail(normalisation.ErrorCode ?? ErrorCodes.UnknownError,
                    "Could not normalise the METS IDs, so the file was not written: "
                    + normalisation.ErrorMessage);
            }

            var report = normalisation.Value;
            if (report.Changed)
            {
                logger?.LogInformation(
                    "Migrated {MetsUri} on write: {IdsRewritten} ID(s) and {ReferencesRewritten} " +
                    "reference(s) rewritten, {WarningCount} warning(s)",
                    fullMets.Uri, report.IdsRewritten, report.ReferencesRewritten,
                    report.Warnings.Count);
                foreach (var warning in report.Warnings)
                {
                    logger?.LogWarning("Migrating {MetsUri} on write: {Warning}", fullMets.Uri, warning);
                }
            }
        }

        return await metsStorage.WriteMets(fullMets);
    }

    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
       return await metsStorage.GetFullMets(metsLocation, eTagToMatch);
    }

    public Result<MetsIdNormalisationReport> NormaliseIds(FullMets fullMets)
    {
        // What was already wrong with this document before we touched it. A document can be
        // imperfect and still worth migrating - a directory div with no ADMID was unnavigable
        // yesterday too - so the test is whether normalising made it worse, not whether the result
        // is perfect.
        var before = fullMets.PathDiagnostics.ToHashSet();

        var report = MetsIdNormaliser.Normalise(fullMets.Mets);
        if (report.DuplicateIds.Count > 0)
        {
            // Already invalid, and not a thing a rewrite can mend: an ID maps to an ID, so both
            // elements would take the same new one and the duplication would stop being visible.
            // Nothing was touched, so the caller still has the document it had. Unprocessable,
            // because the document is the problem, not the platform - and WriteMets relies on
            // this exact code meaning "refused before anything was touched".
            return Result.FailNotNull<MetsIdNormalisationReport>(ErrorCodes.Unprocessable,
                "The METS carries the same ID on more than one element, so its IDs were not "
                + "normalised: " + string.Join("; ", report.DuplicateIds));
        }
        if (!report.Changed)
        {
            return Result.OkNotNull(report);
        }

        // Paths are not touched, so every div should still be reached by the same path. The cache
        // holds div references keyed by path and an ID rewrite is exactly the kind of change a
        // stale cache would hide, so rebuild it - and refuse the whole normalisation if that turns
        // up something new. Nothing has been written yet, so the caller keeps the document it had.
        var introduced = MetsCache.Populate(fullMets).Where(d => !before.Contains(d)).ToList();
        if (introduced.Count > 0)
        {
            return Result.FailNotNull<MetsIdNormalisationReport>(ErrorCodes.UnknownError,
                "Normalising IDs left the METS not navigable by path: " + string.Join("; ", introduced));
        }

        return Result.OkNotNull(report);
    }

    public async Task<Result> HandleSingleFileUpload(Uri workingRoot, WorkingFile workingFile, string depositETag)
    {
        return await HandleSingleChange(workingRoot, depositETag, workingFile, null);
    }

    public async Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag)
    {
        return await HandleSingleChange(workingRoot, depositETag, workingDirectory, null);
    }

    public async Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag)
    {
        return await HandleSingleChange(workingRoot, depositETag, null, localPath);
    }

    private async Task<Result> HandleSingleChange(Uri workingRoot, string? depositETag, WorkingBase? workingBase, string? deletePath)
    {
        var result = await GetFullMets(workingRoot, depositETag);
        if (result.Success)
        {
            var fullMets = result.Value!;

            var editMetsResult = EditMets(workingBase, deletePath, fullMets);
            if (editMetsResult.Success)
            {
                // The write result was previously discarded and Ok returned regardless. That was
                // survivable while WriteMets could only fail on storage, but it can now refuse a
                // document whose IDs would not migrate, and reporting success for a file that was
                // not written is the worst of the available outcomes.
                return await WriteMets(fullMets);
            }

            return editMetsResult;
        }
        return Result.Fail(result.ErrorCode ?? ErrorCodes.UnknownError, result.ErrorMessage);
    }

    public Result AddToMets(FullMets fullMets, WorkingBase workingBase)
    {
        return EditMets(workingBase, null, fullMets);
    }

    public Result DeleteFromMets(FullMets fullMets, string deletePath)
    {
        return EditMets(null, deletePath, fullMets);
    }

    private Result EditMets(WorkingBase? workingBase, string? deletePath, FullMets fullMets)
    {
        // Normalise once so ID minting, FLocat writing and cache keys all use the same
        // canonical deposit-relative form of the path.
        var localPath = MetsCache.NormalisePathKey(workingBase?.LocalPath ?? deletePath) ?? string.Empty;
        var (contextDiv, parent, foundDepth, totalDepth) = LocateMetsDivByLocalPath(fullMets, localPath);

        if (foundDepth < 0)
        {
            // No usable PHYSICAL structMap at all - nothing can be edited
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(fullMets, localPath));
        }

        if (foundDepth == totalDepth)
        {
            if (deletePath is not null)
                return DeleteDiv(contextDiv, fullMets, parent, localPath);
            if (workingBase is WorkingFile workingFile)
                return UpdateExistingFile(contextDiv, fullMets, workingFile, localPath);
            if (workingBase is WorkingDirectory workingDirectory)
                return UpdateExistingDirectory(fullMets, workingDirectory, contextDiv, localPath);
            return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
        }

        if (foundDepth == totalDepth - 1)
        {
            if (deletePath is not null)
                return Result.Fail(ErrorCodes.NotFound, "Can't find a file or folder to delete.");
            if (contextDiv.Type != Constants.DirectoryType)
                return Result.Fail(ErrorCodes.BadRequest, "Parent path is not a Directory");
            if (workingBase is WorkingFile workingFile)
                return AddNewFile(contextDiv, fullMets, workingFile, localPath);
            if (workingBase is WorkingDirectory workingDirectory)
                return AddNewDirectory(contextDiv, fullMets, workingDirectory, localPath);
            return Result.Fail(ErrorCodes.BadRequest, "No working directory or working file supplied to add.");
        }

        return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(fullMets, localPath));
    }

    private static string DescribePathFailure(FullMets fullMets, string localPath)
    {
        var message = $"Could not edit METS because not all parts of the path '{localPath}' have been added to METS.";
        if (fullMets.PathDiagnostics.Count > 0)
        {
            message += " METS path diagnostics: " + string.Join("; ", fullMets.PathDiagnostics);
        }
        return message;
    }

    private Result UpdateExistingFile(DivType contextDiv, FullMets fullMets, WorkingFile workingFile, string localPath)
    {
        if (contextDiv.Type != Constants.ItemType)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");

        var (file, _) = SetFileAndFileGroup(contextDiv, fullMets);
        if (MetsCache.NormalisePathKey(file.FLocat[0].Href) != localPath)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");

        var dmdResult = PopulateDmdFromResource(fullMets, workingFile, contextDiv, localPath);
        if (dmdResult.Failure) return dmdResult;
        return metadataManager.ProcessAllFileMetadata(fullMets, contextDiv, workingFile, localPath);
    }

    private static Result UpdateExistingDirectory(FullMets fullMets, WorkingDirectory workingDirectory, DivType contextDiv, string localPath)
    {
        if (contextDiv.Type != Constants.DirectoryType)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");

        if (workingDirectory.Name.HasText())
            contextDiv.Label = workingDirectory.Name;

        return PopulateDmdFromResource(fullMets, workingDirectory, contextDiv, localPath);
    }

    private Result AddNewFile(DivType parentDiv, FullMets fullMets, WorkingFile workingFile, string localPath)
    {
        var physId = MetsIds.Phys(localPath);
        var fileId = MetsIds.File(localPath);

        var conflict = FindConflictingChild(parentDiv, localPath, fullMets);
        if (conflict != null)
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"METS already contains a div '{conflict}' that could not be resolved to path '{localPath}'.");
        }

        var childItemDiv = new DivType
        {
            Type = Constants.ItemType,
            Label = workingFile.Name ?? localPath.GetSlug(),
            Id = physId,
            Fptr = { new DivTypeFptr { Fileid = fileId } }
        };

        // Every fallible step runs before anything is attached, so a failed add leaves the
        // document and the cache exactly as they were. The order matters: ProcessAllFileMetadata
        // ATTACHES the FILE and the amdSec (it fails, when it fails, before doing so), so it has
        // to come after the descriptive-metadata step rather than before it - otherwise a
        // failure there strands a FILE and an amdSec with no div, and the retry mints a SECOND
        // FILE with the same xs:ID, after which the path can no longer be updated or deleted
        // at all (issue #216).
        //
        // The one thing that does land early is a dmdSec, which the descriptive step may create;
        // if the step after it fails, that section is removed again.
        var dmdSecsBefore = fullMets.Mets.DmdSec.ToHashSet();

        var dmdResult = PopulateDmdFromResource(fullMets, workingFile, childItemDiv, localPath);
        if (dmdResult.Failure)
        {
            RemoveDmdSecsAddedSince(fullMets, dmdSecsBefore);
            return dmdResult;
        }

        var metadataResult = metadataManager.ProcessAllFileMetadata(fullMets, childItemDiv, workingFile, localPath, true);
        if (metadataResult.Failure)
        {
            RemoveDmdSecsAddedSince(fullMets, dmdSecsBefore);
            return metadataResult;
        }

        parentDiv.Div.Add(childItemDiv);
        CacheAddedDiv(fullMets, localPath, childItemDiv);

        SortChildDivs(parentDiv);
        return Result.Ok();
    }

    private Result AddNewDirectory(DivType parentDiv, FullMets fullMets, WorkingDirectory workingDirectory, string localPath)
    {
        var physId = MetsIds.Phys(localPath);
        var admId = MetsIds.Adm(localPath);
        var techId = MetsIds.Tech(localPath);

        var conflict = FindConflictingChild(parentDiv, localPath, fullMets);
        if (conflict != null)
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"METS already contains a div '{conflict}' that could not be resolved to path '{localPath}'.");
        }

        var childDirectoryDiv = new DivType
        {
            Type = Constants.DirectoryType,
            Label = workingDirectory.Name ?? localPath.GetSlug(),
            Id = physId,
            Admid = { admId }
        };

        var premisFile = new FileFormatMetadata
        {
            Source = Constants.Mets,
            OriginalName = localPath,
            StorageLocation = null
        };
        var amdSec = metadataManager.GetAmdSecType(premisFile, admId, techId);
        var dmdResult = PopulateDmdFromResource(fullMets, workingDirectory, childDirectoryDiv, localPath);
        if (dmdResult.Failure) return dmdResult;

        // Attach div, amdSec and cache entry together, after anything that could throw,
        // so a failure cannot leave the tree and the cache out of step (same principle
        // as AddNewFile).
        parentDiv.Div.Add(childDirectoryDiv);
        fullMets.Mets.AmdSec.Add(amdSec);
        CacheAddedDiv(fullMets, localPath, childDirectoryDiv);

        SortChildDivs(parentDiv);
        return Result.Ok();
    }

    /// <summary>
    /// Undo the dmdSecs an abandoned add created — identified by REFERENCE against a snapshot
    /// taken beforehand, not by position. A count-and-truncate would remove whatever happens to
    /// sit at the end, so anything another edit against the same in-memory FullMets legitimately
    /// added in the meantime would be destroyed instead. MetsManager is not thread-safe today
    /// (see the bug/metsmanager-thread-safety work), which is exactly why this should not add a
    /// new way for concurrent edits to lose unrelated metadata.
    /// </summary>
    private static void RemoveDmdSecsAddedSince(FullMets fullMets, HashSet<MdSecType> dmdSecsBefore)
    {
        var added = fullMets.Mets.DmdSec.Where(dmdSec => !dmdSecsBefore.Contains(dmdSec)).ToList();
        foreach (var dmdSec in added)
        {
            fullMets.Mets.DmdSec.Remove(dmdSec);
        }
    }

    /// <summary>
    /// A description of an existing child div that already stands for <paramref name="localPath"/>,
    /// or null if the path is free. Reaching an add for a path whose div already exists means
    /// that div could not be resolved unambiguously - adding anyway would put a second div for
    /// one path into the METS.
    /// </summary>
    /// <remarks>
    /// Three ways a child can already stand for the path, and all three have to be checked:
    /// the encoded ID this add would mint; the raw-path ID carried by documents written before
    /// issue #188 (an encoded mint would not collide with it, so it cannot be caught by the
    /// first check); and - the case IDs cannot catch at all - a div whose own metadata resolves
    /// to this path while carrying an ID of some entirely different scheme. That last one is
    /// exactly what happens when two siblings resolve to one path: navigation rightly refuses
    /// to guess between them, which brings the operation here as an ADD.
    /// </remarks>
    private static string? FindConflictingChild(DivType parentDiv, string localPath, FullMets fullMets)
    {
        var (encodedId, legacyId) = PhysicalDivIdCandidates(localPath);
        var byId = parentDiv.Div.FirstOrDefault(d => d.Id == encodedId || d.Id == legacyId);
        if (byId != null)
        {
            return byId.Id;
        }

        // A complete cache with no entry for this path proves no div resolves to it, so the
        // scan below cannot match. Skipping it keeps the ordinary add - by far the common case -
        // free of a per-sibling resolution.
        if (fullMets.PathDiagnostics.Count == 0 && !fullMets.PhysicalDivsByPath.ContainsKey(localPath))
        {
            return null;
        }

        var index = new MetsIdIndex(fullMets.Mets);
        var byPath = parentDiv.Div.FirstOrDefault(d => MetsCache.TryResolvePath(d, fullMets.Mets, index) == localPath);
        return byPath == null ? null : byPath.Id ?? "(no ID)";
    }

    /// <summary>
    /// Record a newly attached div in the path cache. If a div ELSEWHERE in the tree had
    /// already claimed this path (its metadata resolves a path that disagrees with its tree
    /// position - only producible by edited or third-party METS, since navigation rejects
    /// such an entry as this path's target), a plain overwrite would leave the cache silently
    /// disagreeing with a rebuild while no diagnostic records the duplicate. Rebuild instead:
    /// the duplicate lands on PathDiagnostics, which is what every trust gate tests, so it sees it
    /// and navigation drops to strict per-child resolution for the contested paths.
    /// </summary>
    private static void CacheAddedDiv(FullMets fullMets, string localPath, DivType div)
    {
        if (fullMets.PhysicalDivsByPath.TryGetValue(localPath, out var existing)
            && !ReferenceEquals(existing, div))
        {
            MetsCache.Populate(fullMets);
            return;
        }
        fullMets.PhysicalDivsByPath[localPath] = div;
    }

    private static void SortChildDivs(DivType div)
    {
        var childList = new List<DivType>(div.Div);
        div.Div.Clear();
        foreach (var child in childList.OrderBy(d => d.Label.ToLowerInvariant()))
            div.Div.Add(child);
    }

    private DigitalPreservation.XmlGen.Mets.Mets GetEmptyMets()
    {
        // Every ID is built as prefix + encoded path, even where the path needs no encoding, so
        // that no minting site anywhere concatenates a raw path into an ID (issue #188).
        var objectsIdPart = FolderNames.Objects.ToMetsId();
        var metadataIdPart = FolderNames.Metadata.ToMetsId();
        var adHocIdPart = FolderNames.MetadataAdHoc.ToMetsId();

        var mets = new DigitalPreservation.XmlGen.Mets.Mets
        {
            MetsHdr = new()
            {
                Createdate = DateTime.Now,
                Agent = {
                    new MetsTypeMetsHdrAgent
                    {
                        Role = MetsTypeMetsHdrAgentRole.Creator,
                        Type = MetsTypeMetsHdrAgentType.Other,
                        Othertype = "SOFTWARE",
                        Name = Constants.MetsCreatorAgent
                    }
                }
            },
            DmdSec =
            {
                new MdSecType { Id = Constants.DmdPhysRoot }
            },
            FileSec = new MetsTypeFileSec
            {
                FileGrp =
                {
                    new MetsTypeFileSecFileGrp { Use = Constants.ObjectsFileGrpUse }
                }
            },
            StructMap =
            {
                new StructMapType
                {
                    Type = Constants.Physical,
                    Div = new DivType
                    {
                        Id = "PHYS_ROOT",
                        Label = WorkingDirectory.DefaultRootName,
                        Type = Constants.DirectoryType,
                        Dmdid = { Constants.DmdPhysRoot },
                        Div = {
                            new DivType
                            {
                                Id = Constants.MetadataDivId,
                                Type = Constants.DirectoryType,
                                Label = FolderNames.Metadata,
                                Dmdid = { $"{Constants.DmdIdPrefix}{metadataIdPart}" },
                                Admid = { $"{Constants.AdmIdPrefix}{metadataIdPart}" },
                                Div =
                                    {
                                        new DivType
                                        {
                                            Id = Constants.MetadataAdHocDivId,
                                            Type = Constants.DirectoryType,
                                            Label = FolderNames.AdHoc,
                                            Admid = { $"{Constants.AdmIdPrefix}{adHocIdPart}" },
                                            Dmdid = { $"{Constants.DmdIdPrefix}{adHocIdPart}" },
                                        }
                                    }
                            },
                            new DivType
                            {
                                Id = Constants.ObjectsDivId,
                                Type = Constants.DirectoryType,
                                Label = FolderNames.Objects,
                                Dmdid = { $"{Constants.DmdIdPrefix}{objectsIdPart}" },
                                Admid = { $"{Constants.AdmIdPrefix}{objectsIdPart}" }
                            }
                        }
                    }
                }
            },
            AmdSec =
            {
                metadataManager.GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.Objects
                    },
                    $"{Constants.AdmIdPrefix}{objectsIdPart}", $"{Constants.TechIdPrefix}{objectsIdPart}"),
                metadataManager.GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.Metadata
                    },
                    $"{Constants.AdmIdPrefix}{metadataIdPart}", $"{Constants.TechIdPrefix}{metadataIdPart}"),
                metadataManager.GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.MetadataAdHoc
                    },
                    $"{Constants.AdmIdPrefix}{adHocIdPart}", $"{Constants.TechIdPrefix}{adHocIdPart}")
            }
            // NB we don't have a structLink because we have no logical structMap (yet)
        };

        return mets;
    }


    private static Result DeleteDiv(DivType div, FullMets fullMets, DivType? parent, string? operationPath)
    {
        if (div.Div.Count > 0)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Cannot delete a non-empty directory.");
        }

        // Resolve everything BEFORE mutating anything, so a failed delete leaves the METS
        // exactly as it was.
        FileType? file = null;
        MetsTypeFileSecFileGrp? fileGroup = null;
        IReadOnlyList<string> admTokens;
        if (div is { Type: "Item" })
        {
            (file, fileGroup) = SetFileAndFileGroup(div, fullMets);

            if (MetsCache.NormalisePathKey(file.FLocat[0].Href) != operationPath)
            {
                return Result.Fail(ErrorCodes.BadRequest, "Delete path doesn't match METS flocat");
            }

            admTokens = file.Admid;
        }
        else
        {
            admTokens = div.Admid;
        }

        // ADMID/DMDID are IDREFS token collections (see IdRefs). Deletion is tolerant of a
        // section that doesn't resolve - a dangling reference is exactly the kind of breakage
        // a delete may be cleaning up, and DMDID references dangle by design until metadata
        // is first set (lazy dmdSec creation). A genuine multi-token reference removes EVERY
        // section it resolves - except one that another div or file still references (e.g. a
        // rightsMD shared across files, common in Archivematica METS), which must survive.
        var referencedElsewhere = CollectSectionReferences(fullMets.Mets, [div], file);
        MdSecType? DmdLookup(string id) => fullMets.Mets.DmdSec.FirstOrDefault(d => d.Id == id);
        var amdSecs = IdRefs.ResolveAll(admTokens, id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id))
            .Where(a => !referencedElsewhere.Contains(a.Id))
            .ToList();
        // DMDID can sit on the div or (in third-party shapes) on the FILE element - resolve
        // both, matching what CollectSectionReferences excludes from the survival index.
        var dmdCandidates = IdRefs.ResolveAll(div.Dmdid, DmdLookup);
        if (file is { Dmdid.Count: > 0 })
        {
            dmdCandidates = dmdCandidates.Concat(IdRefs.ResolveAll(file.Dmdid, DmdLookup)).Distinct().ToList();
        }
        var dmdSecs = dmdCandidates
            .Where(d => !referencedElsewhere.Contains(d.Id))
            .ToList();

        fileGroup?.File.Remove(file!);
        if (file?.Id != null)
        {
            RemoveReferencesToFile(fullMets.Mets, file.Id);
        }
        foreach (var amdSec in amdSecs)
        {
            fullMets.Mets.AmdSec.Remove(amdSec);
        }
        foreach (var dmdSec in dmdSecs)
        {
            fullMets.Mets.DmdSec.Remove(dmdSec);
        }

        parent!.Div.Remove(div);
        EvictFromPathCache(fullMets, div);

        return Result.Ok();
    }

    /// <summary>
    /// Drop every structLink and logical-structMap pointer to a FILE element that has just been
    /// removed. Both <c>fptr/@FILEID</c> and <c>smLink/@xlink:from|to</c> are IDREFs, so leaving
    /// them behind makes the document invalid - and worse than invalid: since issue #188 step 2
    /// a file re-added at the same path is minted a NEW (encoded) ID, so the stale reference can
    /// never again be matched, and no API call can remove it. Before step 2 the re-add happened
    /// to reproduce the same raw ID and quietly healed the link.
    /// </summary>
    private static void RemoveReferencesToFile(DigitalPreservation.XmlGen.Mets.Mets mets, string fileId)
    {
        if (mets.StructLink != null)
        {
            var staleLinks = mets.StructLink.SmLink
                .Where(link => link.From == fileId || link.To == fileId)
                .ToList();
            foreach (var link in staleLinks)
            {
                mets.StructLink.SmLink.Remove(link);
            }
        }

        // fptrs live on divs in every structMap - the physical div being deleted takes its own
        // with it, but a LOGICAL div painting this file keeps pointing at a file that is gone.
        // Materialise before mutating - the divs cannot be walked while their fptrs are removed.
        var stalePointers = AllDivs(mets)
            .SelectMany(div => div.Fptr
                .Where(fptr => fptr.Fileid == fileId || fptr.Area?.Fileid == fileId)
                .Select(fptr => (div, fptr)))
            .ToList();
        foreach (var (div, fptr) in stalePointers)
        {
            div.Fptr.Remove(fptr);
        }
    }

    /// <summary>
    /// Every section ID referenced by any div (in any structMap) or file other than the ones
    /// being deleted: each individual token, and the joined form of multi-token collections
    /// (a legacy space-containing ID). Built in ONE traversal so deletion can check each
    /// candidate section in O(1) instead of re-walking the document per section. A section
    /// whose ID is in this set is genuinely shared and must survive the deletion.
    /// </summary>
    internal static HashSet<string> CollectSectionReferences(
        DigitalPreservation.XmlGen.Mets.Mets mets,
        HashSet<DivType> excludedDivs,
        FileType? excludedFile)
    {
        var referencedIds = new HashSet<string>();

        var divs = AllDivs(mets).Where(d => !excludedDivs.Contains(d));
        foreach (var d in divs)
        {
            AddReferences(d.Admid, referencedIds);
            AddReferences(d.Dmdid, referencedIds);
        }

        var files = (mets.FileSec?.FileGrp ?? [])
            .SelectMany(fg => fg.File)
            .Where(f => !ReferenceEquals(f, excludedFile));
        foreach (var f in files)
        {
            AddReferences(f.Admid, referencedIds);
            AddReferences(f.Dmdid, referencedIds);
        }

        return referencedIds;
    }

    private static void AddReferences(System.Collections.ObjectModel.Collection<string> tokens, HashSet<string> referencedIds)
    {
        referencedIds.UnionWith(tokens);
        if (tokens.Count > 1)
        {
            referencedIds.Add(IdRefs.Joined(tokens));
        }
    }

    /// <summary>
    /// Every div in the document, across all structMaps. The generated model types
    /// <c>StructMapType.Div</c> as non-nullable, but a structMap element with no root div is
    /// legal XML and does occur in malformed documents - dereferencing it unguarded is a real
    /// crash, so this is the single place that tolerance lives.
    /// </summary>
    private static IEnumerable<DivType> AllDivs(DigitalPreservation.XmlGen.Mets.Mets mets) =>
        mets.StructMap
            .Select(structMap => structMap.Div)
            .Where(root => root is not null)
            .SelectMany(SelfAndDescendants);

    private static IEnumerable<DivType> SelfAndDescendants(DivType div)
    {
        yield return div;
        foreach (var descendant in div.Div.SelectMany(SelfAndDescendants))
        {
            yield return descendant;
        }
    }

    /// <summary>
    /// Evict every cache entry that points at the deleted div. A div reached via a fallback
    /// tier may own an entry under a DIFFERENT key than the operation path (its own resolved
    /// metadata path), so eviction goes by value, not by the operation path.
    /// </summary>
    private static void EvictFromPathCache(FullMets fullMets, DivType div)
    {
        var keys = fullMets.PhysicalDivsByPath
            .Where(kvp => ReferenceEquals(kvp.Value, div))
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keys)
        {
            fullMets.PhysicalDivsByPath.Remove(key);
        }

        if (fullMets.PathDiagnostics.Count > 0)
        {
            // In a flagged document this delete may have changed what the diagnostics
            // describe: another div may have been claiming an evicted path, or the deleted
            // div may BE the one a diagnostic recorded (deletion is how broken content gets
            // cleaned up). Rebuild so both the cache and the diagnostics reflect post-delete
            // reality instead of load-time state - a fully healed document regains the
            // trusted-cache fast path.
            MetsCache.Populate(fullMets);
        }
    }

    private static (FileType file, MetsTypeFileSecFileGrp fileGroup) SetFileAndFileGroup(DivType div, FullMets fullMets)
    {
        var fileId = div.Fptr[0].Fileid;
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == Constants.ObjectsFileGrpUse);
        var file = fileGroup.File.Single(f => f.Id == fileId);
        return (file, fileGroup);
    }

    private static (DivType contextDiv, DivType? parent, int foundDepth, int totalDepth) LocateMetsDivByLocalPath(FullMets fullMets, string localPath)
    {
        EnsureCache(fullMets);
        AssertCacheConsistent(fullMets);

        localPath = MetsCache.NormalisePathKey(localPath) ?? string.Empty;
        var elements = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // A malformed METS may have no usable PHYSICAL structMap (MetsCache.Populate will have
        // recorded a diagnostic); signal that with foundDepth -1 rather than throwing.
        var physRoot = fullMets.Mets.StructMap.FirstOrDefault(sm => sm.Type == Constants.Physical)?.Div;
        if (physRoot == null)
        {
            return (new DivType(), null, -1, elements.Length);
        }

        var div = physRoot;
        DivType? parent = null;
        var testPath = string.Empty;
        var counter = 0;

        foreach (var element in elements)
        {
            if (testPath.HasText())
            {
                testPath += "/";
            }
            testPath += element;

            // Navigate by path (premis:originalName / FLocat href, via the cache), not by
            // reconstructing div IDs from the path - IDs are opaque (issue #188). The cache
            // fast path is only trusted for a fully clean document: whenever load-time
            // diagnostics exist (duplicate paths, unresolvable divs) every step resolves
            // strictly among the current div's children, so an ambiguous path can never be
            // silently satisfied by whichever div the cache walked first.
            DivType? childDiv;
            DivType? cached = null;
            var cacheIsComplete = fullMets.PathDiagnostics.Count == 0;
            var cacheHit = cacheIsComplete &&
                           fullMets.PhysicalDivsByPath.TryGetValue(testPath, out cached);
            if (cacheHit && div.Div.Contains(cached!))
            {
                childDiv = cached;
            }
            else
            {
                // A complete cache already records the path every div's own metadata resolves
                // to, so on a MISS no sibling can resolve this path and the metadata tier is
                // provably empty - skipping it is what stops an add from resolving every
                // sibling (and rescanning the fileSec for each one). The ID-convention tier
                // still runs: it answers a different question, and dropping it would narrow
                // backwards compatibility rather than just speed things up.
                var metadataTierCanMatch = !cacheIsComplete || cacheHit;
                childDiv = FindChildDivByPath(div, testPath, fullMets.Mets, metadataTierCanMatch);
            }

            if (childDiv is null)
            {
                break;
            }

            counter++;
            parent = div;
            div = childDiv;
        }

        return (div, parent, counter, elements.Length);
    }

    /// <summary>
    /// Fallback resolution among the current div's children when the cache has no usable entry
    /// for a path: first by each child's own path metadata (correct even when an unrelated div
    /// elsewhere claims the same path), then by the ID convention, which keeps divs with broken
    /// path metadata navigable exactly as they were before the cache existed. The convention
    /// tier tries BOTH ID forms - the encoded one minted since issue #188 and the raw-path one
    /// in older documents - because a document can hold both, and a div whose path metadata is
    /// broken is reachable by nothing else. It can be removed after a bulk ID migration
    /// (issue #188 step 3).
    /// </summary>
    private static DivType? FindChildDivByPath(
        DivType parent, string testPath, DigitalPreservation.XmlGen.Mets.Mets mets, bool tryMetadataTier = true)
    {
        // In each tier the match must be UNIQUE among the parent's children - if two children
        // claim the same path or the same conventional ID (corrupted METS), guessing one would
        // silently edit the wrong div; returning null instead surfaces the standard
        // incomplete-path error with the load-time diagnostics attached. An AMBIGUOUS
        // metadata tier is terminal: falling through to the legacy-ID tier would resolve by
        // guesswork exactly the ambiguity this method exists to refuse.
        if (tryMetadataTier)
        {
            // One ID index for the whole sibling scan - resolving each child independently
            // would rescan the fileSec per child.
            var index = new MetsIdIndex(mets);
            var byMetadata = parent.Div
                .Where(d => MetsCache.TryResolvePath(d, mets, index) == testPath)
                .Take(2)
                .ToList();
            if (byMetadata.Count > 0)
            {
                return byMetadata.Count == 1 ? byMetadata[0] : null;
            }
        }
        var (encodedId, legacyId) = PhysicalDivIdCandidates(testPath);
        return UniqueOrNull(parent.Div.Where(d => d.Id == encodedId || d.Id == legacyId));
    }

    /// <summary>
    /// The two ID forms a physical div may carry for one path: the encoded form minted since
    /// issue #188, and the raw-path form in documents written before it.
    /// </summary>
    /// <remarks>
    /// Navigation's ID-convention tier and the duplicate-div guard both accept either form, and
    /// they have to agree on the set — if they disagree, a div one of them can find is invisible
    /// to the other, which is precisely the duplicate-div bug the guard exists to prevent. A
    /// step 3 migration would change what belongs here, so it is one place rather than two.
    /// </remarks>
    private static (string Encoded, string Legacy) PhysicalDivIdCandidates(string localPath) =>
        (MetsIds.Phys(localPath), Constants.PhysIdPrefix + localPath);

    private static DivType? UniqueOrNull(IEnumerable<DivType> divs)
    {
        DivType? found = null;
        foreach (var div in divs)
        {
            if (found != null)
            {
                return null;
            }
            found = div;
        }
        return found;
    }

    /// <summary>
    /// The cache is populated when a METS file is loaded from storage, but a FullMets can also
    /// be constructed directly around an in-memory Mets; a non-empty physical structMap with an
    /// empty cache means that population hasn't happened yet. (An empty cache is never valid
    /// for a managed METS - the metadata/objects template directories are always present.)
    /// </summary>
    private static void EnsureCache(FullMets fullMets)
    {
        if (fullMets.PhysicalDivsByPath.Count == 0)
        {
            MetsCache.Populate(fullMets);
        }
    }

    /// <summary>
    /// Debug-build check that the maintained cache matches a fresh rebuild - catches any
    /// mutation path that forgets to keep <see cref="FullMets.PhysicalDivsByPath"/> up to date.
    /// </summary>
    [Conditional("DEBUG")]
    private static void AssertCacheConsistent(FullMets fullMets)
    {
        var rebuilt = new Dictionary<string, DivType>();
        MetsCache.Build(fullMets.Mets, rebuilt);
        var cache = fullMets.PhysicalDivsByPath;
        var consistent = cache.Count == rebuilt.Count &&
                         cache.All(kvp =>
                             rebuilt.TryGetValue(kvp.Key, out var div) && ReferenceEquals(div, kvp.Value));
        if (!consistent)
        {
            var maintained = string.Join(", ", cache.Keys.Order());
            var expected = string.Join(", ", rebuilt.Keys.Order());
            throw new InvalidOperationException(
                $"PhysicalDivsByPath cache is stale. Maintained: [{maintained}]; rebuilt: [{expected}]. " +
                "A mutation path is missing its cache update.");
        }
    }

    private static DivType? LocateMetsDivByDivId(FullMets fullMets, string divId)
    {
        // Search PHYSICAL structMaps first (there should be one, but a malformed METS may
        // have zero or several - all are searched), then the rest. A structMap element with
        // no root div is skipped rather than dereferenced.
        foreach (var rootDiv in fullMets.Mets.StructMap
                     .OrderByDescending(sm => sm.Type == Constants.Physical)
                     .Select(structMap => structMap.Div))
        {
            if (rootDiv is null)
            {
                continue;
            }
            var found = FindDiv(rootDiv, divId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static DivType? FindDiv(DivType div, string divId)
    {
        if (div.Id == divId)
        {
            return div;
        }

        foreach (var childDiv in div.Div)
        {
            var found = FindDiv(childDiv, divId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// This should be called from four paths:
    ///   Update existing WorkingDirectory
    ///   Update existing WorkingFile
    ///   Add new WorkingDirectory
    ///   Add new WorkingFile
    /// </summary>
    /// <param name="mets"></param>
    /// <param name="resource"></param>
    /// <param name="div"></param>
    /// <param name="localPath">the div's deposit-relative path, already normalised</param>
    private static Result PopulateDmdFromResource(FullMets mets, ResourceBase resource, DivType div, string localPath)
    {
        if (resource.AccessRestrictions != null)
        {
            // If it's an empty array rather than null, this will clear the access restrictions
            var result = SetAccessRestrictionsForDiv(mets, div, resource.AccessRestrictions, localPath);
            if (result.Failure) return result;
        }

        if (resource.RightsStatement != null)
        {
            // OK how to clear a Rights statement?
            var result = SetRightsStatementForDiv(mets, div, resource.RightsStatement, localPath);
            if (result.Failure) return result;
        }

        if (resource.RecordInfo != null)
        {
            // Clear this by passing in a RecordInfo with empty RecordIdentifiers[]
            var result = SetRecordInfoForDiv(mets, div, resource.RecordInfo, localPath);
            if (result.Failure) return result;
        }

        return Result.Ok();
    }

    /// <summary>
    /// The deposit-relative path a div stands for, taken from the path cache. Null for a logical
    /// div (only physical divs are cached) and for a physical div whose path metadata could not
    /// be resolved. Used by the ByDivId entry points, which are given an ID rather than a path,
    /// so that a lazily created dmdSec is still identified by path (issue #188).
    /// </summary>
    /// <remarks>
    /// The reverse scan is deliberate, and is doing more than recovering a path: only physical
    /// divs are in the cache, so a miss is what tells us the div is LOGICAL. Resolving the div's
    /// metadata directly instead (<see cref="MetsCache.TryResolvePath(DivType,
    /// DigitalPreservation.XmlGen.Mets.Mets)"/>) would look like the same answer for less work,
    /// but a logical div of TYPE="Item" carrying an fptr resolves the path of the file it paints
    /// — so it would be handed the DMD_ id of the PHYSICAL div for that path, and the two would
    /// share a dmdSec. Any future change here has to keep the physical/logical distinction.
    ///
    /// Cost is O(cache) per call, which is fine for the one-div-at-a-time callers that exist
    /// today (none is wired to a controller yet). The ByDivId shape is documented on
    /// <see cref="IMetsManager"/> as suiting a caller looping over many div IDs against one
    /// FullMets, and that caller would be O(divs x cache): it should hoist a reverse map, or a
    /// physical-div set, across its loop rather than paying this per div.
    /// </remarks>
    private static string? PathForDiv(FullMets mets, DivType div)
    {
        EnsureCache(mets);
        return mets.PhysicalDivsByPath.FirstOrDefault(kvp => ReferenceEquals(kvp.Value, div)).Key;
    }

    // The failure a Set*ForDiv method returns when the div's descriptive metadata cannot be
    // materialised as MODS (e.g. an existing dmdSec with a non-MODS wrapper or empty xmlData).
    // Success may only be reported when the write actually happened.
    private static Result ModsUnavailable(DivType div) =>
        Result.Fail(ErrorCodes.BadRequest,
            $"The descriptive metadata for div '{div.Id}' could not be read or created as MODS.");

    /// <summary>
    /// Rewrite the derived access conditions (issue #433) across the whole document from each div's
    /// EFFECTIVE chosen conditions - restriction, category and rights statement - so that what is
    /// written agrees with what the parser reads back for every node. The writer OWNS the managed
    /// types (for Leeds, <c>tier</c>) on:
    /// <list type="bullet">
    /// <item>every div that carries a chosen element of its own: the value for its effective inputs
    /// is written;</item>
    /// <item>every FILE div whose effective inputs come partly from its logical range - the parser
    /// falls back to the range for whichever axis the physical tree is silent on, so such a file
    /// cannot inherit the right value from its folder and is given its own;</item>
    /// <item>every div whose dmdSec the platform itself minted: a managed type left there by an
    /// earlier write, now without inputs, is removed so the div inherits like everything else.</item>
    /// </list>
    /// A div with a THIRD PARTY's dmdSec and no chosen inputs is left exactly as found - a Goobi
    /// export carrying only its resulting tier keeps it - as is every non-managed typed condition
    /// everywhere. Called from <see cref="WriteMets"/>; a no-op without a resolver or with nothing
    /// derived.
    /// </summary>
    /// <returns>The IDs of the divs whose MODS changed.</returns>
    public async Task<List<string>> ApplyDerivedAccessConditions(FullMets fullMets)
    {
        var changed = new List<string>();
        if (derivedAccessConditionResolver == null) return changed;
        var managed = await derivedAccessConditionResolver.GetManagedTypesAsync();
        if (managed.Count == 0) return changed;
        var categoryType = await derivedAccessConditionResolver.GetCategoryTypeAsync();

        var pass = new DerivedPass(fullMets, managed, categoryType, changed, PathsByDiv(fullMets));

        // Logical ranges inherit from the physical root (MetsParser.ComputeEffectiveMetadata) and
        // files fall back to their range, so: directories first, then ranges, then files.
        var rootInputs = EffectiveAccessInputs.None;
        var fileDivs = new List<(DivType Div, EffectiveAccessInputs Parent)>();
        var physRoot = fullMets.Mets.StructMap.FirstOrDefault(sm => sm.Type == Constants.Physical)?.Div;
        if (physRoot != null)
        {
            rootInputs = await ApplyDerivedToDirectoryTree(pass, physRoot, rootInputs, fileDivs);
        }
        var logicalInputs = new Dictionary<DivType, EffectiveAccessInputs>(ReferenceEqualityComparer.Instance);
        foreach (var logical in fullMets.Mets.StructMap.Where(sm => sm.Type == Constants.Logical && sm.Div != null))
        {
            await ApplyDerivedToLogicalTree(pass, logical.Div, rootInputs, logicalInputs);
        }
        var wholeFileRanges = WholeFileRangesByFileId(logicalInputs.Keys);
        foreach (var (div, parent) in fileDivs)
        {
            await ApplyDerivedToFileDiv(pass, div, parent, wholeFileRanges, logicalInputs);
        }

        if (changed.Count > 0 && logger?.IsEnabled(LogLevel.Information) == true)
        {
            logger.LogInformation("Derived access conditions rewritten on {Count} div(s) in {MetsUri}",
                changed.Count, fullMets.Uri);
        }
        return changed;
    }

    /// <summary>The <c>@type</c> of the installation's chosen category axis, or null without one.</summary>
    public Task<string?> GetAccessCategoryTypeAsync() =>
        derivedAccessConditionResolver?.GetCategoryTypeAsync() ?? Task.FromResult<string?>(null);

    /// <summary>Everything one derived pass needs, so that the helpers stay small.</summary>
    private sealed record DerivedPass(
        FullMets Mets,
        IReadOnlyCollection<string> Managed,
        string? CategoryType,
        List<string> Changed,
        Dictionary<DivType, string> PathByDiv);

    /// <summary>The chosen elements a div carries itself.</summary>
    private sealed record ChosenInputs(List<string> Restrictions, string? Category, bool RightsPresent, Uri? Rights)
    {
        public static readonly ChosenInputs Nothing = new([], null, false, null);

        public bool Any => Restrictions.Count > 0 || Category != null || RightsPresent;

        /// <summary>The div's effective inputs: its own where set, else its parent's.</summary>
        public EffectiveAccessInputs ApplyTo(EffectiveAccessInputs parent) => new(
            Restrictions.Count > 0 ? Restrictions : parent.Restrictions,
            Category ?? parent.Category,
            RightsPresent ? Rights : parent.RightsStatement);
    }

    private static ChosenInputs ReadChosenInputs(DerivedPass pass, XmlGen.Mods.V3.ModsDefinition mods)
    {
        // Only the platform's own chosen elements are inputs. A Goobi "status" element, which the
        // parser DISPLAYS as a restriction, is not: the writer never derives from a div it did not
        // itself set access metadata on.
        var restrictions = mods.GetAccessConditions(Constants.RestrictionOnAccess);
        var category = pass.CategoryType == null ? null : mods.GetAccessConditions(pass.CategoryType).FirstOrDefault();
        var rightsElements = mods.GetAccessConditions(Constants.UseAndReproduction);
        Uri? rights = null;
        if (rightsElements.Count > 0)
        {
            // Present but not a URI is the "explicitly none" sentinel: effective rights null, and
            // inheritance stops here - the same reading as the parser's.
            Uri.TryCreate(rightsElements[0], UriKind.Absolute, out rights);
        }
        return new ChosenInputs(restrictions, category, rightsElements.Count > 0, rights);
    }

    private async Task<EffectiveAccessInputs> ApplyDerivedToDirectoryTree(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent, List<(DivType Div, EffectiveAccessInputs Parent)> fileDivs)
    {
        if (div.Fptr.Count > 0)
        {
            // A file: handled last, once the logical ranges it may fall back to are known.
            fileDivs.Add((div, parent));
            return parent;
        }
        var effective = await ApplyDerivedToDiv(pass, div, parent);
        foreach (var child in div.Div)
        {
            await ApplyDerivedToDirectoryTree(pass, child, effective, fileDivs);
        }
        return effective;
    }

    private async Task ApplyDerivedToLogicalTree(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var effective = await ApplyDerivedToDiv(pass, div, parent);
        logicalInputs[div] = effective;
        foreach (var child in div.Div)
        {
            await ApplyDerivedToLogicalTree(pass, child, effective, logicalInputs);
        }
    }

    /// <summary>A directory or logical div: its managed types follow its own chosen inputs.</summary>
    private async Task<EffectiveAccessInputs> ApplyDerivedToDiv(DerivedPass pass, DivType div, EffectiveAccessInputs parent)
    {
        var mods = ModsManager.GetModsForDiv(pass.Mets.Mets, div);
        if (mods == null) return parent;
        var chosen = ReadChosenInputs(pass, mods);
        var effective = chosen.ApplyTo(parent);
        await RewriteManaged(pass, div, mods, chosen.Any ? effective : null);
        return effective;
    }

    /// <summary>
    /// A file div: for an axis the physical tree is silent on, its effective input comes from its
    /// single whole-file logical range (MetsParser.SetFileEffective), in which case it is given
    /// its own managed types even without chosen inputs, since it cannot inherit the right value.
    /// </summary>
    private async Task ApplyDerivedToFileDiv(
        DerivedPass pass, DivType div, EffectiveAccessInputs parent,
        Dictionary<string, List<DivType>> wholeFileRanges, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var mods = ModsManager.GetModsForDiv(pass.Mets.Mets, div);
        var chosen = mods == null ? ChosenInputs.Nothing : ReadChosenInputs(pass, mods);
        var physical = chosen.ApplyTo(parent);
        var effective = WithLogicalFallback(physical, chosen, div, wholeFileRanges, logicalInputs);

        if (chosen.Any || !effective.SameAs(physical))
        {
            mods ??= ModsManager.GetModsForDiv(pass.Mets.Mets, div, createDmd: true, pass.PathByDiv.GetValueOrDefault(div));
            if (mods == null) return;
            await RewriteManaged(pass, div, mods, effective);
        }
        else if (mods != null)
        {
            await RewriteManaged(pass, div, mods, null);
        }
    }

    private static EffectiveAccessInputs WithLogicalFallback(
        EffectiveAccessInputs physical, ChosenInputs chosen, DivType div,
        Dictionary<string, List<DivType>> wholeFileRanges, Dictionary<DivType, EffectiveAccessInputs> logicalInputs)
    {
        var fileId = div.Fptr[0].Fileid;
        if (fileId == null || !wholeFileRanges.TryGetValue(fileId, out var ranges) || ranges.Count != 1)
        {
            return physical;
        }
        var range = logicalInputs[ranges[0]];
        // Restriction and category: the physical tree (own, then ancestors) wins whenever it says
        // anything at all.
        var restrictions = physical.Restrictions.Count > 0 ? physical.Restrictions : range.Restrictions;
        var category = physical.Category ?? range.Category;
        // Rights: the file's own element (including the "none" sentinel) wins; else the nearest
        // ancestor with a rights statement; else the range's.
        var rights = chosen.RightsPresent ? physical.RightsStatement : physical.RightsStatement ?? range.RightsStatement;
        return new EffectiveAccessInputs(restrictions, category, rights);
    }

    /// <summary>FILEID -> the logical divs that paint the whole file (an fptr with no area).</summary>
    private static Dictionary<string, List<DivType>> WholeFileRangesByFileId(IEnumerable<DivType> logicalDivs)
    {
        var map = new Dictionary<string, List<DivType>>(StringComparer.Ordinal);
        foreach (var div in logicalDivs)
        {
            foreach (var fileId in div.Fptr.Where(f => f.Area == null && f.Fileid != null).Select(f => f.Fileid).Distinct())
            {
                if (!map.TryGetValue(fileId, out var list))
                {
                    map[fileId] = list = [];
                }
                if (!list.Contains(div)) list.Add(div);
            }
        }
        return map;
    }

    /// <summary>
    /// Writes the managed types for <paramref name="inputs"/>; or, when that is null because the
    /// div has no inputs of its own, removes them if the platform minted this div's dmdSec and
    /// otherwise leaves the div as found.
    /// </summary>
    private async Task RewriteManaged(DerivedPass pass, DivType div, XmlGen.Mods.V3.ModsDefinition mods, EffectiveAccessInputs? inputs)
    {
        if (inputs == null && !IsPlatformMintedDmd(pass, div)) return;

        var before = ManagedSnapshot(mods, pass.Managed);
        foreach (var type in pass.Managed)
        {
            mods.RemoveAccessConditions(type);
        }
        if (inputs != null)
        {
            var derived = await derivedAccessConditionResolver!.ResolveAsync(inputs);
            foreach (var type in pass.Managed)
            {
                if (derived.TryGetValue(type, out var value)) mods.AddAccessCondition(value, type);
            }
        }
        if (!ManagedSnapshot(mods, pass.Managed).SequenceEqual(before))
        {
            ModsManager.SetModsForDiv(pass.Mets.Mets, div, mods);
            pass.Changed.Add(div.Id ?? string.Empty);
        }
    }

    /// <summary>
    /// Whether this div's dmdSec is one the platform minted - <see cref="MetsIds.Dmd"/> of its
    /// path, <see cref="MetsIds.DmdFromDivId"/> of a logical div's ID, the root's, or an anonymous
    /// one. The writer owns the managed types in those and only those: a third party's dmdSec is
    /// never stripped of a value it arrived with.
    /// </summary>
    private static bool IsPlatformMintedDmd(DerivedPass pass, DivType div)
    {
        var dmdId = IdRefs.ResolveSingle(div.Dmdid, id => pass.Mets.Mets.DmdSec.FirstOrDefault(x => x.Id == id))?.Id;
        if (dmdId == null) return false;
        if (dmdId == Constants.DmdPhysRoot || dmdId.StartsWith(Constants.DmdIdPrefix + "ANON_", StringComparison.Ordinal))
        {
            return true;
        }
        if (pass.PathByDiv.TryGetValue(div, out var path))
        {
            return dmdId == MetsIds.Dmd(path);
        }
        return div.Id.HasText() && dmdId == MetsIds.DmdFromDivId(div.Id);
    }

    private static Dictionary<DivType, string> PathsByDiv(FullMets fullMets)
    {
        EnsureCache(fullMets);
        var map = new Dictionary<DivType, string>(ReferenceEqualityComparer.Instance);
        foreach (var (path, div) in fullMets.PhysicalDivsByPath)
        {
            map[div] = path;
        }
        return map;
    }

    // ---- The chosen category (issue #433 review): a third chosen element, written under the
    // ---- profile's configured type and inherited like the restriction. ------------------------

    /// <summary>
    /// Set, or with null clear, the node's chosen category (<c>mods:accessCondition type="{categoryType}"</c>).
    /// <paramref name="categoryType"/> comes from <see cref="GetAccessCategoryTypeAsync"/>; with none
    /// configured a non-null value is refused and null is a no-op.
    /// </summary>
    public Result SetAccessCategoryByPath(FullMets mets, string localPath, string? categoryType, string? category)
    {
        if (categoryType == null)
        {
            return category == null
                ? Result.Ok()
                : Result.Fail(ErrorCodes.BadRequest, "This installation has no access category axis configured.");
        }
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SetAccessCategoryForDiv(mets, div, categoryType, category, MetsCache.NormalisePathKey(localPath));
    }

    private static Result SetAccessCategoryForDiv(FullMets mets, DivType div, string categoryType, string? category, string? localPath)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true, localPath);
        if (mods is null) return ModsUnavailable(div);
        mods.RemoveAccessConditions(categoryType);
        if (category.HasText())
        {
            mods.AddAccessCondition(category, categoryType);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    private static List<string> ManagedSnapshot(XmlGen.Mods.V3.ModsDefinition mods, IReadOnlyCollection<string> managed) =>
        managed.Select(type => type + "=" + string.Join("|", mods.GetAccessConditions(type))).ToList();

    public Result SetRecordInfoByPath(FullMets mets, string localPath, RecordInfo recordInfo)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        // A partial walk means div is an ANCESTOR of the requested path - never write to it.
        // BadRequest for consistency with EditMets, which reports the same condition.
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SetRecordInfoForDiv(mets, div, recordInfo, MetsCache.NormalisePathKey(localPath));
    }

    public Result SetRecordInfoByDivId(FullMets mets, string divId, RecordInfo recordInfo)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetRecordInfoForDiv(mets, div, recordInfo, PathForDiv(mets, div));
    }

    private static Result SetRecordInfoForDiv(FullMets mets, DivType div, RecordInfo recordInfo, string? localPath)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true, localPath);
        if (mods is null) return ModsUnavailable(div);

        mods.SetRecordInfo(recordInfo);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    public Result SetRightsStatementByPath(FullMets mets, string localPath, Uri? rightsStatement)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SetRightsStatementForDiv(mets, div, rightsStatement, MetsCache.NormalisePathKey(localPath));
    }

    public Result SetRightsStatementByDivId(FullMets mets, string divId, Uri? rightsStatement)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetRightsStatementForDiv(mets, div, rightsStatement, PathForDiv(mets, div));
    }

    // Writes a UseAndReproduction element with a non-URI sentinel value so that the
    // parser sees an explicit rights decision and suppresses inheritance, without
    // asserting any particular rights URI. Distinct from SetRightsStatementByPath(null),
    // which removes the element and allows parent rights to flow through.
    public Result SuppressRightsInheritanceByPath(FullMets mets, string localPath)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SuppressRightsInheritanceForDiv(mets, div, MetsCache.NormalisePathKey(localPath));
    }

    public Result SuppressRightsInheritanceByDivId(FullMets mets, string divId)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SuppressRightsInheritanceForDiv(mets, div, PathForDiv(mets, div));
    }

    private static Result SuppressRightsInheritanceForDiv(FullMets mets, DivType div, string? localPath)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true, localPath);
        if (mods is null) return ModsUnavailable(div);
        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        mods.AddAccessCondition(Constants.NullRightsStatement, Constants.UseAndReproduction);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    private static Result SetRightsStatementForDiv(FullMets mets, DivType div, Uri? rightsStatement, string? localPath)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true, localPath);
        if (mods is null) return ModsUnavailable(div);

        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        if (rightsStatement is not null)
        {
            mods.AddAccessCondition(rightsStatement.ToString(), Constants.UseAndReproduction);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }


    public Result SetAccessRestrictionsByPath(FullMets mets, string localPath, List<string> accessRestrictions)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SetAccessRestrictionsForDiv(mets, div, accessRestrictions, MetsCache.NormalisePathKey(localPath));
    }

    public Result SetAccessRestrictionsByDivId(FullMets mets, string divId, List<string> accessRestrictions)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetAccessRestrictionsForDiv(mets, div, accessRestrictions, PathForDiv(mets, div));
    }

    private static Result SetAccessRestrictionsForDiv(FullMets mets, DivType div, List<string> accessRestrictions, string? localPath)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true, localPath);
        if (mods is null) return ModsUnavailable(div);

        mods.RemoveAccessConditions(Constants.RestrictionOnAccess);
        foreach (var accessRestriction in accessRestrictions)
        {
            mods.AddAccessCondition(accessRestriction, Constants.RestrictionOnAccess);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    public Result SetStructMap(FullMets mets, LogicalRange logSm, string? categoryType = null)
    {
        // Logical div IDs are the client's, not ours: they are written into the METS verbatim
        // and the client round-trips them, so an invalid one is REJECTED rather than encoded -
        // silently changing an ID would break the client's own references. This is the only
        // route by which a caller-supplied string becomes an xs:ID (issue #188).
        var invalid = FindInvalidRangeId(logSm);
        if (invalid != null)
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"Logical structMap range ID '{invalid}' is not a valid XML name.");
        }

        var duplicate = FindDuplicateRangeId(mets, logSm);
        if (duplicate != null)
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"Logical structMap range ID '{duplicate}' is already used in this METS file.");
        }

        var existing = mets.Mets.StructMap
            .FirstOrDefault(sm => sm.Type == Constants.Logical && SameStructMapId(sm.Div?.Id, logSm.Id));
        if (existing != null)
        {
            RemoveLogicalStructMapDmdSecs(mets, existing.Div);
            mets.Mets.StructMap.Remove(existing);
        }

        mets.Mets.StructMap.Add(new StructMapType
        {
            Type = Constants.Logical,
            Div = BuildLogicalDiv(mets, logSm, categoryType)
        });
        return Result.Ok();
    }

    /// <summary>
    /// Match the root IDs of two logical structMaps, treating absent and empty as the same
    /// thing. A logical div in third-party METS may carry no ID at all; the parser surfaces
    /// that as an empty string and this class writes it back as no attribute, so both forms
    /// must go on identifying the same structMap for replace and remove.
    /// </summary>
    private static bool SameStructMapId(string? a, string? b) =>
        (a ?? string.Empty) == (b ?? string.Empty);

    /// <summary>
    /// The first range ID in the tree that could not be used as an xs:ID, or null if all are
    /// usable. An absent ID (null, or the empty string the parser reports for a logical div
    /// with no ID attribute) is not a rejection: BuildLogicalDiv writes no ID attribute for it.
    /// </summary>
    private static string? FindInvalidRangeId(LogicalRange range)
    {
        if (range.Id.HasText() && !IsValidNCName(range.Id))
        {
            return range.Id;
        }
        return range.Ranges.Select(FindInvalidRangeId).FirstOrDefault(invalid => invalid != null);
    }

    /// <summary>
    /// The first range ID that would not be unique in the document, or null if all are.
    /// An xs:ID is an NCName AND unique - validating only the first half would still let a
    /// caller write a document no schema-aware consumer can load. Both halves of the check
    /// matter in practice: the UI mints LOG_ + epoch-milliseconds, so two ranges created in the
    /// same millisecond collide with each other.
    /// </summary>
    /// <remarks>
    /// IDs already in the structMap being REPLACED don't count as taken - re-applying an edited
    /// structMap under its own ID is the normal path, and that map is removed before the new
    /// one is written.
    /// </remarks>
    private static string? FindDuplicateRangeId(FullMets mets, LogicalRange logSm)
    {
        var replaced = mets.Mets.StructMap
            .FirstOrDefault(sm => sm.Type == Constants.Logical && SameStructMapId(sm.Div?.Id, logSm.Id));
        var replacedDivs = replaced?.Div == null
            ? []
            : new HashSet<DivType>(SelfAndDescendants(replaced.Div));

        var taken = new HashSet<string>(AllDivs(mets.Mets)
            .Where(d => !replacedDivs.Contains(d) && d.Id.HasText())
            .Select(d => d.Id));

        return FindFirstTakenId(logSm, taken);
    }

    private static string? FindFirstTakenId(LogicalRange range, HashSet<string> taken)
    {
        // An ID-less range takes no ID, so several of them are not a collision with each other.
        if (range.Id.HasText() && !taken.Add(range.Id))
        {
            return range.Id;
        }
        return range.Ranges.Select(child => FindFirstTakenId(child, taken)).FirstOrDefault(found => found != null);
    }

    private static bool IsValidNCName(string candidate)
    {
        try
        {
            XmlConvert.VerifyNCName(candidate);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static DivType BuildLogicalDiv(FullMets mets, LogicalRange range, string? categoryType)
    {
        var div = new DivType
        {
            // An empty ID would be written as ID="", which is not a valid xs:ID. A logical div
            // with no ID is a real shape - third-party METS often has one, and the parser
            // reports it as an empty string - so it is written back as no ID attribute rather
            // than an empty one (issue #188).
            Id = range.Id.HasText() ? range.Id : null,
            Type = range.Type,
            Label = range.Name
        };

        var category = categoryType == null ? null : range.OtherAccessConditions?.GetValueOrDefault(categoryType);
        bool needsMods = range.Name != null || range.RecordInfo != null
            || range.AccessRestrictions is { Count: > 0 } || range.RightsStatement != null || category != null;
        if (needsMods)
        {
            var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true)!;
            mods.SetTitle(range.Name ?? string.Empty);
            if (range.RecordInfo != null)
                mods.SetRecordInfo(range.RecordInfo);
            ModsManager.SetModsForDiv(mets.Mets, div, mods);
        }

        // A logical div has no deposit-relative path: its dmdSec is identified from the
        // client-supplied div ID, which SetStructMap has validated as an NCName.
        if (range.AccessRestrictions is { Count: > 0 })
            SetAccessRestrictionsForDiv(mets, div, range.AccessRestrictions, null);

        if (range.RightsStatement != null)
            SetRightsStatementForDiv(mets, div, range.RightsStatement, null);

        // The chosen category (issue #433) travels in OtherAccessConditions under the profile's type;
        // any other entry there is derived or third-party and is not written from the client's copy.
        if (category != null)
            SetAccessCategoryForDiv(mets, div, categoryType!, category, null);

        foreach (var fp in range.Files)
            div.Fptr.Add(BuildFptr(mets.Mets, fp));

        foreach (var child in range.Ranges)
            div.Div.Add(BuildLogicalDiv(mets, child, categoryType));

        return div;
    }

    /// <summary>
    /// The ID of the FILE element in the fileSec that stands for a deposit-relative path -
    /// looked up by FLocat href, never reconstructed from the path. A METS file written before
    /// issue #188 carries raw-path FILE IDs, so minting the encoded form here would produce
    /// fptr/smLink references to IDs that do not exist in the document (and would make the
    /// existing raw-ID smLinks unremovable, since removal matches on the same derivation).
    /// Minting is the last resort, for a path with no FILE element at all; the caller is then
    /// writing a reference the fileSec has yet to catch up with, exactly as before.
    /// </summary>
    private static string ResolveFileId(DigitalPreservation.XmlGen.Mets.Mets mets, string? localPath)
    {
        var normalised = MetsCache.NormalisePathKey(localPath);
        if (normalised == null)
        {
            return MetsIds.File(string.Empty);
        }

        // The OBJECTS group holds the preserved files; other groups (THUMBS, ALTO, derivatives
        // in third-party METS) can carry an entry with the SAME href for a derivative of the
        // same source. A link must name the master, so OBJECTS is searched first and the other
        // groups only as a fallback - matching SetFileAndFileGroup, which requires OBJECTS
        // outright. First-wins within a group, the same convention navigation uses for a
        // malformed document.
        var fileGrps = mets.FileSec?.FileGrp ?? [];
        var file = fileGrps.Where(fg => fg.Use == Constants.ObjectsFileGrpUse).SelectMany(fg => fg.File)
                       .FirstOrDefault(f => HrefMatches(f, normalised))
                   ?? fileGrps.Where(fg => fg.Use != Constants.ObjectsFileGrpUse).SelectMany(fg => fg.File)
                       .FirstOrDefault(f => HrefMatches(f, normalised));

        return file?.Id ?? MetsIds.File(normalised);
    }

    private static bool HrefMatches(FileType file, string normalisedPath) =>
        MetsCache.NormalisePathKey(file.FLocat.FirstOrDefault()?.Href) == normalisedPath;

    private static DivTypeFptr BuildFptr(DigitalPreservation.XmlGen.Mets.Mets mets, FilePointer fp)
    {
        var fileId = ResolveFileId(mets, fp.LocalPath);

        if (fp.BeginTime.HasValue || fp.EndTime.HasValue)
        {
            var area = new AreaType
            {
                Fileid = fileId,
                Betype = AreaTypeBetype.Time,
                Begin = fp.BeginTime.HasValue ? MetsTimeCode.FromSeconds(fp.BeginTime.Value) : null,
                End = fp.EndTime.HasValue ? MetsTimeCode.FromSeconds(fp.EndTime.Value) : null
            };
            if (fp.Region != null)
            {
                area.Shape = AreaTypeShape.Rect;
                area.Coords = $"{fp.Region.X1},{fp.Region.Y1},{fp.Region.X2},{fp.Region.Y2}";
            }
            return new DivTypeFptr { Area = area };
        }

        if (fp.Region != null)
        {
            return new DivTypeFptr
            {
                Area = new AreaType
                {
                    Fileid = fileId,
                    Shape = AreaTypeShape.Rect,
                    Coords = $"{fp.Region.X1},{fp.Region.Y1},{fp.Region.X2},{fp.Region.Y2}"
                }
            };
        }

        if (fp.ExtraAreaAttributes is { Count: > 0 })
        {
            var area = new AreaType { Fileid = fileId };
            var xmlDoc = new XmlDocument();
            foreach (var (name, value) in fp.ExtraAreaAttributes)
            {
                var attr = xmlDoc.CreateAttribute(name);
                attr.Value = value;
                area.AnyAttribute.Add(attr);
            }
            return new DivTypeFptr { Area = area };
        }

        return new DivTypeFptr { Fileid = fileId };
    }

    private static void RemoveLogicalStructMapDmdSecs(FullMets mets, DivType root)
    {
        // DMDID is an IDREFS token collection (see IdRefs): resolve it properly so a legacy
        // space-containing dmdSec ID (split into tokens by the XmlSerializer) is found and
        // removed, not silently orphaned. A dmdSec still referenced from outside the structMap
        // being removed (e.g. shared with a physical div) survives.
        var structMapDivs = new HashSet<DivType>(SelfAndDescendants(root));
        var referencedElsewhere = CollectSectionReferences(mets.Mets, structMapDivs, null);
        foreach (var div in structMapDivs)
        {
            var dmdSecs = IdRefs
                .ResolveAll(div.Dmdid, id => mets.Mets.DmdSec.FirstOrDefault(d => d.Id == id))
                .Where(d => !referencedElsewhere.Contains(d.Id));
            foreach (var dmdSec in dmdSecs)
                mets.Mets.DmdSec.Remove(dmdSec);
        }
    }

    public void SetStructMapOrder(FullMets mets, string[] ids)
    {
        // Tolerate malformed logical structMaps rather than throwing: one with no root div
        // (or no root ID) cannot be ordered, and only the first of two claiming the same ID
        // is ordered - the same first-wins convention navigation uses.
        var logicalMaps = new Dictionary<string, StructMapType>();
        foreach (var sm in mets.Mets.StructMap.Where(sm => sm.Type == Constants.Logical && sm.Div?.Id != null))
            logicalMaps.TryAdd(sm.Div.Id, sm);

        foreach (var map in logicalMaps.Values)
            mets.Mets.StructMap.Remove(map);

        foreach (var id in ids)
        {
            if (logicalMaps.TryGetValue(id, out var map))
                mets.Mets.StructMap.Add(map);
        }
    }

    public void RemoveStructMap(FullMets mets, string id)
    {
        var existing = mets.Mets.StructMap
            .FirstOrDefault(sm => sm.Type == Constants.Logical && SameStructMapId(sm.Div?.Id, id));
        if (existing == null) return;

        RemoveLogicalStructMapDmdSecs(mets, existing.Div);
        mets.Mets.StructMap.Remove(existing);
    }

    // smLinks reference the FILE elements the fileSec actually holds, so both ends are resolved
    // by path (see ResolveFileId) rather than reconstructed - which keeps links to legacy
    // raw-ID files correct, and removable. Paths are normalised first (./, BagIt data/ prefix)
    // so the variants the setters accept all resolve to the same file.
    public void LinkFile(FullMets mets, string from, string to, Uri role)
    {
        mets.Mets.StructLink ??= new MetsTypeStructLink();
        mets.Mets.StructLink.SmLink.Add(new StructLinkTypeSmLink
        {
            From = ResolveFileId(mets.Mets, from),
            To = ResolveFileId(mets.Mets, to),
            Arcrole = role.ToString()
        });
    }

    public void UnLinkFile(FullMets mets, string from, string to, Uri role)
    {
        if (mets.Mets.StructLink == null) return;

        var fromId = ResolveFileId(mets.Mets, from);
        var toId = ResolveFileId(mets.Mets, to);
        var arcrole = role.ToString();

        var link = mets.Mets.StructLink.SmLink
            .FirstOrDefault(sl => sl.From == fromId && sl.To == toId && sl.Arcrole == arcrole);
        if (link != null)
            mets.Mets.StructLink.SmLink.Remove(link);
    }

    public void SetFileLinks(FullMets mets, string localPath, List<FileLink> links)
    {
        // Remove all existing outgoing smLinks from this file
        if (mets.Mets.StructLink != null)
        {
            var fromId = ResolveFileId(mets.Mets, localPath);
            var toRemove = mets.Mets.StructLink.SmLink.Where(sl => sl.From == fromId).ToList();
            foreach (var sl in toRemove)
                mets.Mets.StructLink.SmLink.Remove(sl);
        }
        // Add the new links
        foreach (var link in links.Where(l => l.Role != null))
            LinkFile(mets, localPath, link.To, link.Role!);
    }
}
