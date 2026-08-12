using System.Diagnostics;
using System.Xml;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Mets;

public class MetsManager(
    IMetsParser metsParser,
    IMetsStorage metsStorage,
    MetadataManager metadataManager) : IMetsManager
{
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


    public async Task<Result> WriteMets(FullMets fullMets)
    {
        return await metsStorage.WriteMets(fullMets);
    }

    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
       return await metsStorage.GetFullMets(metsLocation, eTagToMatch);
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
                await WriteMets(fullMets);
                return Result.Ok();
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
                return UpdateExistingDirectory(fullMets, workingDirectory, contextDiv);
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

        var dmdResult = PopulateDmdFromResource(fullMets, workingFile, contextDiv);
        if (dmdResult.Failure) return dmdResult;
        return metadataManager.ProcessAllFileMetadata(fullMets, contextDiv, workingFile, localPath);
    }

    private static Result UpdateExistingDirectory(FullMets fullMets, WorkingDirectory workingDirectory, DivType contextDiv)
    {
        if (contextDiv.Type != Constants.DirectoryType)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");

        if (workingDirectory.Name.HasText())
            contextDiv.Label = workingDirectory.Name;

        return PopulateDmdFromResource(fullMets, workingDirectory, contextDiv);
    }

    private Result AddNewFile(DivType parentDiv, FullMets fullMets, WorkingFile workingFile, string localPath)
    {
        var physId = Constants.PhysIdPrefix + localPath;
        var fileId = Constants.FileIdPrefix + localPath;

        // Reaching an add for a path whose div already exists means that div could not be
        // resolved by path OR by the legacy ID fallback - adding again would write duplicate
        // ID attributes into the METS. Refuse rather than corrupt.
        if (parentDiv.Div.Any(d => d.Id == physId))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"METS already contains a div '{physId}' that could not be resolved to path '{localPath}'.");
        }

        var childItemDiv = new DivType
        {
            Type = Constants.ItemType,
            Label = workingFile.Name ?? localPath.GetSlug(),
            Id = physId,
            Fptr = { new DivTypeFptr { Fileid = fileId } }
        };

        // Nothing is attached to the METS until the fallible metadata step has succeeded,
        // so a failed add leaves the document (and the cache) exactly as it was.
        var metadataResult = metadataManager.ProcessAllFileMetadata(fullMets, childItemDiv, workingFile, localPath, true);
        if (metadataResult.Failure)
            return metadataResult;

        var dmdResult = PopulateDmdFromResource(fullMets, workingFile, childItemDiv);
        if (dmdResult.Failure) return dmdResult;

        parentDiv.Div.Add(childItemDiv);
        fullMets.PhysicalDivsByPath[localPath] = childItemDiv;

        SortChildDivs(parentDiv);
        return Result.Ok();
    }

    private Result AddNewDirectory(DivType parentDiv, FullMets fullMets, WorkingDirectory workingDirectory, string localPath)
    {
        var physId = Constants.PhysIdPrefix + localPath;
        var admId = Constants.AdmIdPrefix + localPath;
        var techId = Constants.TechIdPrefix + localPath;

        if (parentDiv.Div.Any(d => d.Id == physId))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"METS already contains a div '{physId}' that could not be resolved to path '{localPath}'.");
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
        var dmdResult = PopulateDmdFromResource(fullMets, workingDirectory, childDirectoryDiv);
        if (dmdResult.Failure) return dmdResult;

        // Attach div, amdSec and cache entry together, after anything that could throw,
        // so a failure cannot leave the tree and the cache out of step (same principle
        // as AddNewFile).
        parentDiv.Div.Add(childDirectoryDiv);
        fullMets.Mets.AmdSec.Add(amdSec);
        fullMets.PhysicalDivsByPath[localPath] = childDirectoryDiv;

        SortChildDivs(parentDiv);
        return Result.Ok();
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
                    new MetsTypeFileSecFileGrp { Use = "OBJECTS" }
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
                                Dmdid = { $"{Constants.DmdIdPrefix}{FolderNames.Metadata}" },
                                Admid = { $"{Constants.AdmIdPrefix}{FolderNames.Metadata}" },
                                Div = 
                                    {
                                        new DivType
                                        {
                                            Id = Constants.MetadataAdHocDivId,
                                            Type = Constants.DirectoryType,
                                            Label = FolderNames.AdHoc,
                                            Admid = { $"{Constants.AdmIdPrefix}{FolderNames.MetadataAdHoc}" },
                                            Dmdid = { $"{Constants.DmdIdPrefix}{FolderNames.MetadataAdHoc}" },
                                        }
                                    }
                            },
                            new DivType
                            {
                                Id = Constants.ObjectsDivId,
                                Type = Constants.DirectoryType,
                                Label = FolderNames.Objects,
                                Dmdid = { $"{Constants.DmdIdPrefix}{FolderNames.Objects}" },
                                Admid = { $"{Constants.AdmIdPrefix}{FolderNames.Objects}" }
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
                    $"{Constants.AdmIdPrefix}{FolderNames.Objects}", $"{Constants.TechIdPrefix}{FolderNames.Objects}"),
                metadataManager.GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.Metadata
                    },
                    $"{Constants.AdmIdPrefix}{FolderNames.Metadata}", $"{Constants.TechIdPrefix}{FolderNames.Metadata}"),
                metadataManager.GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.MetadataAdHoc
                    },
                    $"{Constants.AdmIdPrefix}{FolderNames.MetadataAdHoc}", $"{Constants.TechIdPrefix}{FolderNames.MetadataAdHoc}")
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
        var amdSecs = IdRefs.ResolveAll(admTokens, id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id))
            .Where(a => !referencedElsewhere.Contains(a.Id))
            .ToList();
        var dmdSecs = IdRefs.ResolveAll(div.Dmdid, id => fullMets.Mets.DmdSec.FirstOrDefault(d => d.Id == id))
            .Where(d => !referencedElsewhere.Contains(d.Id))
            .ToList();

        fileGroup?.File.Remove(file!);
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
    /// Every section ID referenced by any div (in any structMap) or file other than the ones
    /// being deleted: each individual token, and the joined form of multi-token collections
    /// (a legacy space-containing ID). Built in ONE traversal so deletion can check each
    /// candidate section in O(1) instead of re-walking the document per section. A section
    /// whose ID is in this set is genuinely shared and must survive the deletion.
    /// </summary>
    private static HashSet<string> CollectSectionReferences(
        DigitalPreservation.XmlGen.Mets.Mets mets,
        HashSet<DivType> excludedDivs,
        FileType? excludedFile)
    {
        var referencedIds = new HashSet<string>();

        var divs = mets.StructMap
            .Where(sm => sm.Div != null)
            .SelectMany(sm => SelfAndDescendants(sm.Div))
            .Where(d => !excludedDivs.Contains(d));
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

    private static void AddReferences(IReadOnlyList<string> tokens, HashSet<string> referencedIds)
    {
        foreach (var token in tokens)
        {
            referencedIds.Add(token);
        }
        if (tokens.Count > 1)
        {
            referencedIds.Add(IdRefs.Joined(tokens));
        }
    }

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

        if (keys.Count > 0 && fullMets.HasDuplicatePaths)
        {
            // In a duplicate-path document another div may have been claiming one of the
            // evicted paths - rebuild so the cache reflects post-delete reality.
            MetsCache.Populate(fullMets);
        }
    }

    private static (FileType file, MetsTypeFileSecFileGrp fileGroup) SetFileAndFileGroup(DivType div, FullMets fullMets)
    {
        var fileId = div.Fptr[0].Fileid;
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
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
            DivType? childDiv = null;
            if (fullMets.PathDiagnostics.Count == 0 &&
                fullMets.PhysicalDivsByPath.TryGetValue(testPath, out var cached) &&
                div.Div.Contains(cached))
            {
                childDiv = cached;
            }
            childDiv ??= FindChildDivByPath(div, testPath, fullMets.Mets);

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
    /// elsewhere claims the same path), then by the legacy ID convention, which keeps divs with
    /// broken path metadata navigable exactly as they were before the cache existed. The legacy
    /// tier can be removed after a bulk ID migration (issue #188 step 3).
    /// </summary>
    private static DivType? FindChildDivByPath(DivType parent, string testPath, DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        // In each tier the match must be UNIQUE among the parent's children - if two children
        // claim the same path or the same conventional ID (corrupted METS), guessing one would
        // silently edit the wrong div; returning null instead surfaces the standard
        // incomplete-path error with the load-time diagnostics attached. An AMBIGUOUS
        // metadata tier is terminal: falling through to the legacy-ID tier would resolve by
        // guesswork exactly the ambiguity this method exists to refuse.
        var byMetadata = parent.Div
            .Where(d => MetsCache.TryResolvePath(d, mets) == testPath)
            .Take(2)
            .ToList();
        if (byMetadata.Count > 0)
        {
            return byMetadata.Count == 1 ? byMetadata[0] : null;
        }
        return UniqueOrNull(parent.Div.Where(d => d.Id == $"{Constants.PhysIdPrefix}{testPath}"));
    }

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
    private static Result PopulateDmdFromResource(FullMets mets, ResourceBase resource, DivType div)
    {
        if (resource.AccessRestrictions != null)
        {
            // If it's an empty array rather than null, this will clear the access restrictions
            var result = SetAccessRestrictionsForDiv(mets, div, resource.AccessRestrictions);
            if (result.Failure) return result;
        }

        if (resource.RightsStatement != null)
        {
            // OK how to clear a Rights statement?
            var result = SetRightsStatementForDiv(mets, div, resource.RightsStatement);
            if (result.Failure) return result;
        }

        if (resource.RecordInfo != null)
        {
            // Clear this by passing in a RecordInfo with empty RecordIdentifiers[]
            var result = SetRecordInfoForDiv(mets, div, resource.RecordInfo);
            if (result.Failure) return result;
        }

        return Result.Ok();
    }

    // The failure a Set*ForDiv method returns when the div's descriptive metadata cannot be
    // materialised as MODS (e.g. an existing dmdSec with a non-MODS wrapper or empty xmlData).
    // Success may only be reported when the write actually happened.
    private static Result ModsUnavailable(DivType div) =>
        Result.Fail(ErrorCodes.BadRequest,
            $"The descriptive metadata for div '{div.Id}' could not be read or created as MODS.");

    public Result SetRecordInfoByPath(FullMets mets, string localPath, RecordInfo recordInfo)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        // A partial walk means div is an ANCESTOR of the requested path - never write to it.
        // BadRequest for consistency with EditMets, which reports the same condition.
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.BadRequest, DescribePathFailure(mets, localPath));
        return SetRecordInfoForDiv(mets, div, recordInfo);
    }

    public Result SetRecordInfoByDivId(FullMets mets, string divId, RecordInfo recordInfo)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetRecordInfoForDiv(mets, div, recordInfo);
    }

    private static Result SetRecordInfoForDiv(FullMets mets, DivType div, RecordInfo recordInfo)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
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
        return SetRightsStatementForDiv(mets, div, rightsStatement);
    }

    public Result SetRightsStatementByDivId(FullMets mets, string divId, Uri? rightsStatement)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetRightsStatementForDiv(mets, div, rightsStatement);
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
        return SuppressRightsInheritanceForDiv(mets, div);
    }

    public Result SuppressRightsInheritanceByDivId(FullMets mets, string divId)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SuppressRightsInheritanceForDiv(mets, div);
    }

    private static Result SuppressRightsInheritanceForDiv(FullMets mets, DivType div)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true);
        if (mods is null) return ModsUnavailable(div);
        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        mods.AddAccessCondition(Constants.NullRightsStatement, Constants.UseAndReproduction);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    private static Result SetRightsStatementForDiv(FullMets mets, DivType div, Uri? rightsStatement)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
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
        return SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
    }

    public Result SetAccessRestrictionsByDivId(FullMets mets, string divId, List<string> accessRestrictions)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        return SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
    }

    private static Result SetAccessRestrictionsForDiv(FullMets mets, DivType div, List<string> accessRestrictions)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
        if (mods is null) return ModsUnavailable(div);

        mods.RemoveAccessConditions(Constants.RestrictionOnAccess);
        foreach (var accessRestriction in accessRestrictions)
        {
            mods.AddAccessCondition(accessRestriction, Constants.RestrictionOnAccess);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
        return Result.Ok();
    }

    public void SetStructMap(FullMets mets, LogicalRange logSm)
    {
        var existing = mets.Mets.StructMap
            .FirstOrDefault(sm => sm.Type == Constants.Logical && sm.Div?.Id == logSm.Id);
        if (existing != null)
        {
            RemoveLogicalStructMapDmdSecs(mets, existing.Div);
            mets.Mets.StructMap.Remove(existing);
        }

        mets.Mets.StructMap.Add(new StructMapType
        {
            Type = Constants.Logical,
            Div = BuildLogicalDiv(mets, logSm)
        });
    }

    private static DivType BuildLogicalDiv(FullMets mets, LogicalRange range)
    {
        var div = new DivType
        {
            Id = range.Id,
            Type = range.Type,
            Label = range.Name
        };

        bool needsMods = range.Name != null || range.RecordInfo != null
            || range.AccessRestrictions is { Count: > 0 } || range.RightsStatement != null;
        if (needsMods)
        {
            var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true)!;
            mods.SetTitle(range.Name ?? string.Empty);
            if (range.RecordInfo != null)
                mods.SetRecordInfo(range.RecordInfo);
            ModsManager.SetModsForDiv(mets.Mets, div, mods);
        }

        if (range.AccessRestrictions is { Count: > 0 })
            SetAccessRestrictionsForDiv(mets, div, range.AccessRestrictions);

        if (range.RightsStatement != null)
            SetRightsStatementForDiv(mets, div, range.RightsStatement);

        foreach (var fp in range.Files)
            div.Fptr.Add(BuildFptr(fp));

        foreach (var child in range.Ranges)
            div.Div.Add(BuildLogicalDiv(mets, child));

        return div;
    }

    private static DivTypeFptr BuildFptr(FilePointer fp)
    {
        var fileId = Constants.FileIdPrefix + fp.LocalPath;

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
        var logicalMaps = mets.Mets.StructMap
            .Where(sm => sm.Type == Constants.Logical)
            .ToDictionary(sm => sm.Div.Id);

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
            .FirstOrDefault(sm => sm.Type == Constants.Logical && sm.Div?.Id == id);
        if (existing == null) return;

        RemoveLogicalStructMapDmdSecs(mets, existing.Div);
        mets.Mets.StructMap.Remove(existing);
    }

    // FILE_ ids are minted from paths; normalise incoming paths the same way navigation and
    // ID minting do, so that a path variant the setters accept (./, BagIt data/ prefix)
    // cannot produce smLinks referencing FILE ids that don't exist.
    public void LinkFile(FullMets mets, string from, string to, Uri role)
    {
        mets.Mets.StructLink ??= new MetsTypeStructLink();
        mets.Mets.StructLink.SmLink.Add(new StructLinkTypeSmLink
        {
            From = Constants.FileIdPrefix + MetsCache.NormalisePathKey(from),
            To = Constants.FileIdPrefix + MetsCache.NormalisePathKey(to),
            Arcrole = role.ToString()
        });
    }

    public void UnLinkFile(FullMets mets, string from, string to, Uri role)
    {
        if (mets.Mets.StructLink == null) return;

        var fromId = Constants.FileIdPrefix + MetsCache.NormalisePathKey(from);
        var toId = Constants.FileIdPrefix + MetsCache.NormalisePathKey(to);
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
            var fromId = Constants.FileIdPrefix + MetsCache.NormalisePathKey(localPath);
            var toRemove = mets.Mets.StructLink.SmLink.Where(sl => sl.From == fromId).ToList();
            foreach (var sl in toRemove)
                mets.Mets.StructLink.SmLink.Remove(sl);
        }
        // Add the new links
        foreach (var link in links.Where(l => l.Role != null))
            LinkFile(mets, localPath, link.To, link.Role!);
    }
}
