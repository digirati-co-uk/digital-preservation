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

        PopulateDmdFromResource(fullMets, workingFile, contextDiv);
        return metadataManager.ProcessAllFileMetadata(fullMets, contextDiv, workingFile, localPath);
    }

    private static Result UpdateExistingDirectory(FullMets fullMets, WorkingDirectory workingDirectory, DivType contextDiv)
    {
        if (contextDiv.Type != Constants.DirectoryType)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");

        if (workingDirectory.Name.HasText())
            contextDiv.Label = workingDirectory.Name;

        PopulateDmdFromResource(fullMets, workingDirectory, contextDiv);
        return Result.Ok();
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

        PopulateDmdFromResource(fullMets, workingFile, childItemDiv);
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
        PopulateDmdFromResource(fullMets, workingDirectory, childDirectoryDiv);

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

        string? admId;
        if (div is { Type: "Item" })
        {
            var (file, fileGroup) = SetFileAndFileGroup(div, fullMets);

            if (MetsCache.NormalisePathKey(file.FLocat[0].Href) != operationPath)
            {
                return Result.Fail(ErrorCodes.BadRequest, "Delete path doesn't match METS flocat");
            }

            admId = file.Admid.Count > 1 ? string.Join(" ", file.Admid) : file.Admid[0];

            fileGroup.File.Remove(file);
        }
        else
        {
            admId = div.Admid.Count > 1 ? string.Join(" ", div.Admid) : div.Admid[0];
        }

        // for both Files and Directories
        var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == admId);
        fullMets.Mets.AmdSec.Remove(amdSec);

        if (div.Dmdid.Count != 0)
        {
            var dmdId = div.Dmdid.Count > 1 ? string.Join(" ", div.Dmdid) : div.Dmdid[0];
            var dmdSec = fullMets.Mets.DmdSec.Single(d => d.Id == dmdId);
            fullMets.Mets.DmdSec.Remove(dmdSec);
        }

        parent!.Div.Remove(div);

        // Only evict the cache entry if it is actually THIS div's - when the deleted div was
        // reached via a fallback tier (its own path metadata being broken), the key may map to
        // a different div that legitimately owns the path.
        if (fullMets.PhysicalDivsByPath.TryGetValue(operationPath!, out var cachedDiv) &&
            ReferenceEquals(cachedDiv, div))
        {
            fullMets.PhysicalDivsByPath.Remove(operationPath!);
            if (fullMets.PathDiagnostics.Count > 0)
            {
                // A malformed doc may contain another div that claimed the same path (see the
                // duplicate-path diagnostics) - rebuild so the cache reflects post-delete reality.
                MetsCache.Populate(fullMets);
            }
        }

        return Result.Ok();
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
            // reconstructing div IDs from the path - IDs are opaque (issue #188). The cached
            // div is only accepted if it really is a child of the current div; a malformed
            // source can have two unrelated divs resolving to the same path, and the cache
            // holds whichever was walked first.
            if (!fullMets.PhysicalDivsByPath.TryGetValue(testPath, out var childDiv) ||
                !div.Div.Contains(childDiv))
            {
                childDiv = FindChildDivByPath(div, testPath, fullMets.Mets);
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
    /// elsewhere claims the same path), then by the legacy ID convention, which keeps divs with
    /// broken path metadata navigable exactly as they were before the cache existed. The legacy
    /// tier can be removed after a bulk ID migration (issue #188 step 3).
    /// </summary>
    private static DivType? FindChildDivByPath(DivType parent, string testPath, DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        // In each tier the match must be UNIQUE among the parent's children - if two children
        // claim the same path or the same conventional ID (corrupted METS), guessing one would
        // silently edit the wrong div; returning null instead surfaces the standard
        // incomplete-path error with the load-time diagnostics attached.
        var byMetadata = UniqueOrNull(parent.Div.Where(d => MetsCache.TryResolvePath(d, mets) == testPath));
        return byMetadata ?? UniqueOrNull(parent.Div.Where(d => d.Id == $"{Constants.PhysIdPrefix}{testPath}"));
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
        // Look in the physical structMap first (should be only one; tolerate zero or many
        // in a malformed METS - same policy as LocateMetsDivByLocalPath and MetsCache)
        var physDiv = fullMets.Mets.StructMap.FirstOrDefault(sm => sm.Type == Constants.Physical)?.Div;
        if (physDiv != null)
        {
            var foundInPhysical = FindDiv(physDiv, divId);
            if (foundInPhysical != null)
            {
                return foundInPhysical;
            }
        }

        foreach (var smType in fullMets.Mets.StructMap.Where(sm => sm.Type != Constants.Physical))
        {
            var foundInOther = FindDiv(smType.Div, divId);
            if (foundInOther != null)
            {
                return foundInOther;
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
    private static void PopulateDmdFromResource(FullMets mets, ResourceBase resource, DivType div)
    {
        if (resource.AccessRestrictions != null)
        {
            // If it's an empty array rather than null, this will clear the access restrictions
            SetAccessRestrictionsForDiv(mets, div, resource.AccessRestrictions);
        }

        if (resource.RightsStatement != null)
        {
            // OK how to clear a Rights statement?
            SetRightsStatementForDiv(mets, div, resource.RightsStatement);
        }

        if (resource.RecordInfo != null)
        {
            // Clear this by passing in a RecordInfo with empty RecordIdentifiers[]
            SetRecordInfoForDiv(mets, div, resource.RecordInfo);
        }
    }

    public Result SetRecordInfoByPath(FullMets mets, string localPath, RecordInfo recordInfo)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        // A partial walk means div is an ANCESTOR of the requested path - never write to it
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.NotFound, DescribePathFailure(mets, localPath));
        SetRecordInfoForDiv(mets, div, recordInfo);
        return Result.Ok();
    }

    public Result SetRecordInfoByDivId(FullMets mets, string divId, RecordInfo recordInfo)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        SetRecordInfoForDiv(mets, div, recordInfo);
        return Result.Ok();
    }

    private static void SetRecordInfoForDiv(FullMets mets, DivType div, RecordInfo recordInfo)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
        if (mods is null) return;

        mods.SetRecordInfo(recordInfo);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
    }

    public Result SetRightsStatementByPath(FullMets mets, string localPath, Uri? rightsStatement)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.NotFound, DescribePathFailure(mets, localPath));
        SetRightsStatementForDiv(mets, div, rightsStatement);
        return Result.Ok();
    }

    public Result SetRightsStatementByDivId(FullMets mets, string divId, Uri? rightsStatement)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        SetRightsStatementForDiv(mets, div, rightsStatement);
        return Result.Ok();
    }

    // Writes a UseAndReproduction element with a non-URI sentinel value so that the
    // parser sees an explicit rights decision and suppresses inheritance, without
    // asserting any particular rights URI. Distinct from SetRightsStatementByPath(null),
    // which removes the element and allows parent rights to flow through.
    public Result SuppressRightsInheritanceByPath(FullMets mets, string localPath)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.NotFound, DescribePathFailure(mets, localPath));
        SuppressRightsInheritanceForDiv(mets, div);
        return Result.Ok();
    }

    public Result SuppressRightsInheritanceByDivId(FullMets mets, string divId)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        SuppressRightsInheritanceForDiv(mets, div);
        return Result.Ok();
    }

    private static void SuppressRightsInheritanceForDiv(FullMets mets, DivType div)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd: true);
        if (mods is null) return;
        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        mods.AddAccessCondition(Constants.NullRightsStatement, Constants.UseAndReproduction);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
    }

    private static void SetRightsStatementForDiv(FullMets mets, DivType div, Uri? rightsStatement)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
        if (mods is null) return;

        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        if (rightsStatement is not null)
        {
            mods.AddAccessCondition(rightsStatement.ToString(), Constants.UseAndReproduction);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
    }


    public Result SetAccessRestrictionsByPath(FullMets mets, string localPath, List<string> accessRestrictions)
    {
        var (div, _, foundDepth, totalDepth) = LocateMetsDivByLocalPath(mets, localPath);
        if (foundDepth != totalDepth)
            return Result.Fail(ErrorCodes.NotFound, DescribePathFailure(mets, localPath));
        SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
        return Result.Ok();
    }

    public Result SetAccessRestrictionsByDivId(FullMets mets, string divId, List<string> accessRestrictions)
    {
        var div = LocateMetsDivByDivId(mets, divId);
        if (div is null)
            return Result.Fail(ErrorCodes.NotFound, $"No div with ID '{divId}' in METS");
        SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
        return Result.Ok();
    }

    private static void SetAccessRestrictionsForDiv(FullMets mets, DivType div, List<string> accessRestrictions)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
        if (mods is null) return;

        mods.RemoveAccessConditions(Constants.RestrictionOnAccess);
        foreach (var accessRestriction in accessRestrictions)
        {
            mods.AddAccessCondition(accessRestriction, Constants.RestrictionOnAccess);
        }
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
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

    private static void RemoveLogicalStructMapDmdSecs(FullMets mets, DivType div)
    {
        foreach (var dmdId in div.Dmdid)
        {
            var dmdSec = mets.Mets.DmdSec.FirstOrDefault(d => d.Id == dmdId);
            if (dmdSec != null)
                mets.Mets.DmdSec.Remove(dmdSec);
        }
        foreach (var child in div.Div)
            RemoveLogicalStructMapDmdSecs(mets, child);
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

    public void LinkFile(FullMets mets, string from, string to, Uri role)
    {
        mets.Mets.StructLink ??= new MetsTypeStructLink();
        mets.Mets.StructLink.SmLink.Add(new StructLinkTypeSmLink
        {
            From = Constants.FileIdPrefix + from,
            To = Constants.FileIdPrefix + to,
            Arcrole = role.ToString()
        });
    }

    public void UnLinkFile(FullMets mets, string from, string to, Uri role)
    {
        if (mets.Mets.StructLink == null) return;

        var fromId = Constants.FileIdPrefix + from;
        var toId = Constants.FileIdPrefix + to;
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
            var fromId = Constants.FileIdPrefix + localPath;
            var toRemove = mets.Mets.StructLink.SmLink.Where(sl => sl.From == fromId).ToList();
            foreach (var sl in toRemove)
                mets.Mets.StructLink.SmLink.Remove(sl);
        }
        // Add the new links
        foreach (var link in links.Where(l => l.Role != null))
            LinkFile(mets, localPath, link.To, link.Role!);
    }
}
