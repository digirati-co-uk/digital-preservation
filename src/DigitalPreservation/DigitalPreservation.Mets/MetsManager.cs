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
        var localPath = FolderNames.RemovePathPrefix(workingBase?.LocalPath ?? deletePath)!;
        var (contextDiv, parent, foundDepth, totalDepth) = LocateMetsDivByLocalPath(fullMets, localPath);

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

        return Result.Fail(ErrorCodes.BadRequest,
            $"Could not edit METS because not all parts of the path '{localPath}' have been added to METS.");
    }

    private Result UpdateExistingFile(DivType contextDiv, FullMets fullMets, WorkingFile workingFile, string localPath)
    {
        if (contextDiv.Type != Constants.ItemType)
            return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");

        var (file, _) = SetFileAndFileGroup(contextDiv, fullMets);
        if (file.FLocat[0].Href != localPath)
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

        var childItemDiv = new DivType
        {
            Type = Constants.ItemType,
            Label = workingFile.Name ?? localPath.GetSlug(),
            Id = physId,
            Fptr = { new DivTypeFptr { Fileid = fileId } }
        };
        parentDiv.Div.Add(childItemDiv);

        PopulateDmdFromResource(fullMets, workingFile, childItemDiv);
        var metadataResult = metadataManager.ProcessAllFileMetadata(fullMets, childItemDiv, workingFile, localPath, true);
        if (metadataResult.Failure)
            return metadataResult;

        SortChildDivs(parentDiv);
        return Result.Ok();
    }

    private Result AddNewDirectory(DivType parentDiv, FullMets fullMets, WorkingDirectory workingDirectory, string localPath)
    {
        var physId = Constants.PhysIdPrefix + localPath;
        var admId = Constants.AdmIdPrefix + localPath;
        var techId = Constants.TechIdPrefix + localPath;

        var childDirectoryDiv = new DivType
        {
            Type = Constants.DirectoryType,
            Label = workingDirectory.Name ?? localPath.GetSlug(),
            Id = physId,
            Admid = { admId }
        };
        parentDiv.Div.Add(childDirectoryDiv);

        var premisFile = new FileFormatMetadata
        {
            Source = Constants.Mets,
            OriginalName = localPath,
            StorageLocation = null
        };
        fullMets.Mets.AmdSec.Add(metadataManager.GetAmdSecType(premisFile, admId, techId));
        PopulateDmdFromResource(fullMets, workingDirectory, childDirectoryDiv);

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
                                            Admid = { $"{Constants.DmdIdPrefix}{FolderNames.MetadataAdHoc}" },
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

            if (file.FLocat[0].Href != operationPath)
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
        var elements = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var div = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
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
            // This is navigating using our ID convention for directories
            // If we don't want to do this, we can use the premis:originalName for the directory
            var childDiv = div.Div.SingleOrDefault(d => d.Id == $"{Constants.PhysIdPrefix}{testPath}");
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

    private static DivType? LocateMetsDivByDivId(FullMets fullMets, string divId)
    {
        // look in the physical structMap first, there should be only one
        var physDiv = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var foundInPhysical = FindDiv(physDiv, divId);
        if (foundInPhysical != null)
        {
            return foundInPhysical;
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

    public void SetRecordInfoByPath(FullMets mets, string localPath, RecordInfo recordInfo)
    {
        var (div, _, _, _) = LocateMetsDivByLocalPath(mets, localPath);
        SetRecordInfoForDiv(mets, div, recordInfo);
    }

    public void SetRecordInfoByDivId(FullMets mets, string divId, RecordInfo recordInfo)
    {
        var div = LocateMetsDivByDivId(mets, divId)!;
        SetRecordInfoForDiv(mets, div, recordInfo);
    }

    private static void SetRecordInfoForDiv(FullMets mets, DivType div, RecordInfo recordInfo)
    {
        var mods = ModsManager.GetModsForDiv(mets.Mets, div, createDmd:true);
        if (mods is null) return;

        mods.SetRecordInfo(recordInfo);
        ModsManager.SetModsForDiv(mets.Mets, div, mods);
    }

    public void SetRightsStatementByPath(FullMets mets, string localPath, Uri? rightsStatement)
    {
        var (div, _, _, _) = LocateMetsDivByLocalPath(mets, localPath);
        SetRightsStatementForDiv(mets, div, rightsStatement);
    }

    public void SetRightsStatementByDivId(FullMets mets, string divId, Uri? rightsStatement)
    {
        var div = LocateMetsDivByDivId(mets, divId)!;
        SetRightsStatementForDiv(mets, div, rightsStatement);
    }

    // Writes a UseAndReproduction element with a non-URI sentinel value so that the
    // parser sees an explicit rights decision and suppresses inheritance, without
    // asserting any particular rights URI. Distinct from SetRightsStatementByPath(null),
    // which removes the element and allows parent rights to flow through.
    public void SuppressRightsInheritanceByPath(FullMets mets, string localPath)
    {
        var (div, _, _, _) = LocateMetsDivByLocalPath(mets, localPath);
        SuppressRightsInheritanceForDiv(mets, div);
    }

    public void SuppressRightsInheritanceByDivId(FullMets mets, string divId)
    {
        var div = LocateMetsDivByDivId(mets, divId)!;
        SuppressRightsInheritanceForDiv(mets, div);
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


    public void SetAccessRestrictionsByPath(FullMets mets, string localPath, List<string> accessRestrictions)
    {
        var (div, _, _, _) = LocateMetsDivByLocalPath(mets, localPath);
        SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
    }

    public void SetAccessRestrictionsByDivId(FullMets mets, string divId, List<string> accessRestrictions)
    {
        var div = LocateMetsDivByDivId(mets, divId)!;
        SetAccessRestrictionsForDiv(mets, div, accessRestrictions);
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
            return new DivTypeFptr
            {
                Area = new AreaType
                {
                    Fileid = fileId,
                    Betype = AreaTypeBetype.Time,
                    Begin = fp.BeginTime.HasValue ? MetsTimeCode.FromSeconds(fp.BeginTime.Value) : null,
                    End = fp.EndTime.HasValue ? MetsTimeCode.FromSeconds(fp.EndTime.Value) : null
                }
            };
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
