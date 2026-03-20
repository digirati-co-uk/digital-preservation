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
        // Add workingBase to METS
        // This is where our non-opaque phys structmap IDs come into play.
        // This may not be a good idea. But without it, we need a more complex way of
        // finding the METS parts, like the XDocument parsing in MetsParser.

        var localPath = FolderNames.RemovePathPrefix(workingBase?.LocalPath ?? deletePath)!;
        var (div, parent, foundDepth, totalDepth) = LocateMetsDivByLocalPath(fullMets, localPath);

        // div might be the file itself, or a parent or grandparent directory.
        // But for this atomic upload file handler we will not allow any Directory creation, that
        // must have already happened in HandleCreateFolder
        // i.e., we must already be at the last or penultimate member of elements
        if (foundDepth == totalDepth)
        {
            if (deletePath is not null)
            {
                // we have arrived at an existing file or folder which is being DELETED
                if (workingBase is null)
                {
                    return DeleteDiv(div, fullMets, parent, localPath);
                }
                return Result.Fail(ErrorCodes.BadRequest, "Cannot supply a WorkingBase and a deletePath.");
            }

            // we have arrived at an existing file or folder which is being OVERWRITTEN
            if (workingBase is not WorkingDirectory || div.Type == "Directory")
            {
                if (workingBase is not WorkingFile workingFile)
                {
                    if (workingBase is WorkingDirectory workingDirectory)
                    {
                        if (div.Type != "Directory")
                            return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");

                        // Is there anything else that could be done here?
                        if (workingDirectory.Name.HasText())
                        {
                            // and is it even done on hasText?
                            div.Label = workingDirectory.Name;
                        }
                        
                        // UPDATE an existing Directory
                        PopulateDmdFromResource(fullMets, workingDirectory, div);
                    }
                    else
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
                    }
                }
                else
                {
                    if (div.Type != "Item")
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");

                    var (file, _) = SetFileAndFileGroup(div, fullMets);

                    if (file.FLocat[0].Href != localPath)
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");

                    // UPDATE AN EXISTING FILE
                    PopulateDmdFromResource(fullMets, workingFile, div);
                    return metadataManager.ProcessAllFileMetadata(fullMets, div, workingFile, localPath);

                }
            }
            else
            {
                return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");
            }
        }
        else if (foundDepth == totalDepth - 1)
        {
            if (deletePath is not null)
                return Result.Fail(ErrorCodes.NotFound, "Can't find a file or folder to delete.");

            // div is a directory
            if (div.Type != "Directory")
                return Result.Fail(ErrorCodes.BadRequest, "Parent path is not a Directory");

            if (workingBase is null)
                return Result.Fail(ErrorCodes.BadRequest, "No working directory or working file supplied to add.");

            var physId = Constants.PhysIdPrefix + localPath;
            var admId = Constants.AdmIdPrefix + localPath;
            var techId = Constants.TechIdPrefix + localPath;

            if (workingBase is not WorkingFile workingFile)
            {
                if (workingBase is WorkingDirectory workingDirectory)
                {
                    // Add a new Directory Div 
                    var childDirectoryDiv = new DivType
                    {
                        Type = Constants.DirectoryType,
                        Label = workingDirectory.Name ?? localPath.GetSlug(),
                        Id = physId,
                        Admid = { admId }
                    };
                    div.Div.Add(childDirectoryDiv);
                    var premisFile = new FileFormatMetadata
                    {
                        Source = Constants.Mets,
                        OriginalName = localPath, // workingDirectory.LocalPath
                        StorageLocation = null // storageLocation
                    };
                    fullMets.Mets.AmdSec.Add(metadataManager.GetAmdSecType(premisFile, admId, techId));
                    PopulateDmdFromResource(fullMets, workingDirectory, childDirectoryDiv);
                }
                else
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
                }
            }
            else
            {
                // Add a new Directory (Item) Div 
                var fileId = Constants.FileIdPrefix + localPath;
                var childItemDiv = new DivType
                {
                    Type = Constants.ItemType,
                    Label = workingFile.Name ?? localPath.GetSlug(),
                    Id = physId,
                    Fptr = { new DivTypeFptr { Fileid = fileId } }
                };
                div.Div.Add(childItemDiv);

                PopulateDmdFromResource(fullMets, workingFile, div);
                var metadataResult = metadataManager.ProcessAllFileMetadata(fullMets, childItemDiv, workingFile, localPath, true);
                if (metadataResult.Failure)
                    return metadataResult;
            }

            // Now we need to ensure the child items are in alphanumeric order by name...
            // how do we do that? We can't sort a Collection<T> in place, and we can't 
            // create a new Collection and assign it to Div

            var childList = new List<DivType>(div.Div);
            div.Div.Clear();
            // We will order case-insensitive; we want to match what a typical file explorer would do.
            // What about the original ordering? For born digital, is there such a thing?
            // How do we know what sort order the creator of the archive applied to their view?
            // Later we can implement something that can set order.
            foreach (var divType in childList.OrderBy(d => d.Label.ToLowerInvariant()))
            {
                div.Div.Add(divType);
            }
        }
        else
        {
            var message = "Could not edit METS because not all parts of the path '" + localPath +
                          "' have been added to METS.";
            return Result.Fail(ErrorCodes.BadRequest, message);
        }

        return Result.Ok();
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
                    Type = "PHYSICAL",
                    Div = new DivType
                    {
                        Id = "PHYS_ROOT",
                        Label = WorkingDirectory.DefaultRootName,
                        Type = Constants.DirectoryType,  
                        Dmdid = { Constants.DmdPhysRoot },
                        Div = { 
                            new DivType
                            {
                                Id = Constants.MetadataDivId,  // do this with premis:originalName metadata for directories?
                                Type = Constants.DirectoryType,
                                Label = FolderNames.Metadata,
                                Dmdid = { $"{Constants.DmdIdPrefix}{FolderNames.Metadata}" },
                                Admid = { $"{Constants.AdmIdPrefix}{FolderNames.Metadata}" }
                            }, 
                            new DivType
                            {
                                Id = Constants.ObjectsDivId,  // do this with premis:originalName metadata for directories?
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
                    $"{Constants.AdmIdPrefix}{FolderNames.Metadata}", $"{Constants.TechIdPrefix}{FolderNames.Metadata}")
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
        
    private (DivType div, DivType? parent, int foundDepth, int totalDepth) LocateMetsDivByLocalPath(FullMets fullMets, string localPath)
    {
        var elements = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var div = fullMets.Mets.StructMap.Single(sm => sm.Type == "PHYSICAL").Div!;
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

    private DivType? LocateMetsDivByDivId(FullMets fullMets, string divId)
    {
        // look in the physical structMap first, there should be only one
        var physDiv = fullMets.Mets.StructMap.Single(sm => sm.Type == "PHYSICAL").Div!;
        var foundInPhysical = FindDiv(physDiv, divId);
        if (foundInPhysical != null)
        {
            return foundInPhysical;
        }

        foreach (var smType in fullMets.Mets.StructMap.Where(sm => sm.Type != "PHYSICAL"))
        {
            var foundInOther = FindDiv(smType.Div, divId);
            if (foundInOther != null)
            {
                return foundInOther;
            }
        }

        return null;
    }

    private DivType? FindDiv(DivType div, string divId)
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
    private void PopulateDmdFromResource(FullMets mets, ResourceBase resource, DivType div)
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

    private void SetAccessRestrictionsForDiv(FullMets mets, DivType div, List<string> accessRestrictions)
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
        throw new NotImplementedException();
    }

    public void SetStructMapOrder(FullMets mets, string[] ids)
    {
        throw new NotImplementedException();
    }

    public void RemoveStructMap(FullMets mets, string id)
    {
        throw new NotImplementedException();
    }

    public void LinkFile(FullMets mets, string from, string to, Uri role)
    {
        throw new NotImplementedException();
    }

    public void UnLinkFile(FullMets mets, string from, string to, Uri role)
    {
        throw new NotImplementedException();
    }
}