using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Extensions;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;

namespace Storage.Repository.Common.Mets;

public class MetsManager(
    IMetsParser metsParser,
    IMetsStorage metsStorage) : IMetsManager
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

    public async Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit)
    {
        var (file, mets) = await GetStandardMets(metsLocation, agNameFromDeposit);
        
        AddResourceToMets(mets, archivalGroup.Id!, mets.StructMap[0].Div, archivalGroup);
        
        var writeResult = await WriteMets(new FullMets{ Mets = mets, Uri = file });
        if (writeResult.Success)
        {
            return await metsParser.GetMetsFileWrapper(file);
        }
        return Result.FailNotNull<MetsFileWrapper>(writeResult.ErrorCode!, writeResult.ErrorMessage);
    }


    /// <summary>
    /// This builds up the METS file from repository resources, not working files
    /// This will likely never be used in production
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
            DivType? childDirectoryDiv = null;
            if (container is ArchivalGroup && childContainer.GetSlug() == FolderNames.Objects)
            {
                // The objects div should already exist from our template
                childDirectoryDiv = mets.StructMap[0].Div.Div.Single(d => d.Id == Constants.ObjectsDivId);
            }

            if (childDirectoryDiv == null)
            {
                var localPath = childContainer.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");
                var admId = Constants.AdmIdPrefix + localPath;
                var techId = Constants.TechIdPrefix + localPath;
                childDirectoryDiv = new DivType
                {
                    Type = Constants.DirectoryType,
                    Label = childContainer.Name,
                    Id = $"{Constants.PhysIdPrefix}{localPath}",
                    Admid = { admId }
                };
                div.Div.Add(childDirectoryDiv);
                var reducedPremisForObjectDir = new FileFormatMetadata
                {
                    Source = Constants.Mets,
                    OriginalName = localPath,
                    StorageLocation = childContainer.Id
                };
                mets.AmdSec.Add(GetAmdSecType(reducedPremisForObjectDir, admId, techId));
            }
            AddResourceToMets(mets, archivalGroupUri, childDirectoryDiv, childContainer);
        }

        foreach (var binary in container.Binaries)
        {
            var localPath = binary.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");
            if (MetsUtils.IsMetsFile(localPath!, true))
            {
                continue;
            }
            var fileId = Constants.FileIdPrefix + localPath;
            var admId = Constants.AdmIdPrefix + localPath;
            var techId = Constants.TechIdPrefix + localPath;
            var childItemDiv = new DivType
            {
                Type = Constants.ItemType,
                Label = binary.Name,
                Id = $"{Constants.PhysIdPrefix}{localPath}",
                Fptr = { new DivTypeFptr{ Fileid = fileId } }
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
            mets.AmdSec.Add(GetAmdSecType(premisFile, admId, techId));
        }
    }


    private async Task<(Uri file, DigitalPreservation.XmlGen.Mets.Mets mets)> GetStandardMets(Uri metsLocation, string? agNameFromDeposit)
    {
        // might be a file path or an S3 URI
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (root, file) = fileLocResult.Value;
        if (file is null)
        {
            file = new Uri(root + "mets.xml");
        }

        var mets = GetEmptyMets();
        var mods = ModsManager.Create(agNameFromDeposit ?? "[Untitled]");
        ModsManager.SetRootMods(mets, mods);
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

        var (counter, parent, div, elements, operationPath, testPath) = GetMetsElements(workingBase, deletePath, fullMets);

        // div might be the file itself, or a parent or grandparent directory.
        // But for this atomic upload file handler we will not allow any Directory creation, that
        // must have already happened in HandleCreateFolder
        // i.e., we must already be at the last or penultimate member of elements
        if (elements != null && counter == elements.Length)
        {
            if (deletePath is not null)
                return workingBase is null ? DeleteFile(div, fullMets, parent, operationPath) : Result.Fail(ErrorCodes.BadRequest, "Cannot supply a WorkingBase and a deletePath.");

            // we have arrived at an existing file or folder which is being OVERWRITTEN
            if (workingBase is not WorkingDirectory || div?.Type == "Directory")
            {
                if (workingBase is not WorkingFile workingFile)
                {
                    if (workingBase is WorkingDirectory workingDirectory)
                    {
                        if (div?.Type != "Directory")
                            return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");


                        // Is there anything else that could be done here?
                        if (workingDirectory.Name.HasText())
                            // and is it even done on hasText?
                            div.Label = workingDirectory.Name;
                    }
                    else
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
                    }
                }
                else
                {
                    if (div != null && div.Type != "Item")
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");

                    SetFileAndFileGroup(div, fullMets);

                    if (File?.FLocat[0].Href != operationPath)
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");

                    // TODO: This is a quick fix to get round the problem of spaces in XML IDs.
                    // We need to not have any spaces in XML IDs, which means we need to escape them 
                    // in a reversible way (replacing with _ won't do)
                    FileAdmId = string.Join(' ', File.Admid);
                    AmdSec = fullMets.Mets.AmdSec.Single(a => a.Id == FileAdmId);

                    var metadataResult = GetMetadataXml(fullMets, div, operationPath);

                    if (!metadataResult.Success)
                        return metadataResult;

                    ProcessFileFormatDataForFile(workingFile, operationPath, false);
                    ProcessVirusDataForFile(workingFile);
                }
            }
            else
            {
                return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");
            }
        } 
        else if (elements != null && counter == elements.Length - 1)
        {
            if (deletePath is not null)
                return Result.Fail(ErrorCodes.NotFound, "Can't find a file or folder to delete.");
            
            // div is a directory
            if (div != null && div.Type != "Directory")
                return Result.Fail(ErrorCodes.BadRequest, "Parent path is not a Directory");

            if (workingBase is null)
                return Result.Fail(ErrorCodes.BadRequest, "No working directory or working file supplied to add.");

            var physId = Constants.PhysIdPrefix + operationPath;
            var admId = Constants.AdmIdPrefix + operationPath;
            var techId = Constants.TechIdPrefix + operationPath;
            TechId = techId;

            FileAdmId = admId;

            if (workingBase is not WorkingFile workingFile)
            {
                if (workingBase is WorkingDirectory workingDirectory)
                {
                    var childDirectoryDiv = new DivType
                    {
                        Type = Constants.DirectoryType,
                        Label = workingDirectory.Name ?? operationPath.GetSlug(),
                        Id = physId,
                        Admid = { admId }
                    };
                    div?.Div.Add(childDirectoryDiv);
                    var premisFile = new FileFormatMetadata
                    {
                        Source = Constants.Mets,
                        OriginalName = operationPath, // workingDirectory.LocalPath
                        StorageLocation = null // storageLocation
                    };
                    fullMets.Mets.AmdSec.Add(GetAmdSecType(premisFile, admId, techId));
                }
                else
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
                }
            }
            else
            {
                var fileId = Constants.FileIdPrefix + operationPath;
                var childItemDiv = new DivType
                {
                    Type = Constants.ItemType,
                    Label = workingFile.Name ?? operationPath.GetSlug(),
                    Id = physId,
                    Fptr = { new DivTypeFptr { Fileid = fileId } }
                };
                div?.Div.Add(childItemDiv);

                ProcessFileFormatDataForFile(workingFile, operationPath, true);

                File = new FileType
                {
                    Id = fileId,
                    Admid = { admId },
                    Mimetype = PremisFile?.ContentType ?? workingFile.ContentType,
                    FLocat =
                    {
                        new FileTypeFLocat
                        {
                            Href = operationPath, Loctype = FileTypeFLocatLoctype.Url
                        }
                    }
                };

                fullMets.Mets.FileSec.FileGrp[0].File.Add(File);

                if (PremisFile != null && PremisFile.ContentType.HasText() && PremisFile.ContentType != ContentTypes.NotIdentified)
                {
                    File.Mimetype = PremisFile.ContentType;
                }

                ProcessVirusDataForFile(workingFile);

                fullMets.Mets.AmdSec.Add(AmdSec);
            }

            // Now we need to ensure the child items are in alphanumeric order by name...
            // how do we do that? We can't sort a Collection<T> in place, and we can't 
            // create a new Collection and assign it to Div
            if (div != null)
            {
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
        }
        else
        {
            var message = "Could not edit METS because not all parts of the path '" + testPath +
                          "' have been added to METS.";
            return Result.Fail(ErrorCodes.BadRequest, message);
        }

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
                                Dmdid = { $"DMD_{FolderNames.Metadata}" },
                                Admid = { $"ADM_{FolderNames.Metadata}" }
                            }, 
                            new DivType
                            {
                                Id = Constants.ObjectsDivId,  // do this with premis:originalName metadata for directories?
                                Type = Constants.DirectoryType,
                                Label = FolderNames.Objects,
                                Dmdid = { $"DMD_{FolderNames.Objects}" },
                                Admid = { $"ADM_{FolderNames.Objects}" }
                            }
                        }
                    }
                }
            },
            AmdSec =
            {
                GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.Objects 
                    }, 
                    $"{Constants.AdmIdPrefix}{FolderNames.Objects}", $"{Constants.TechIdPrefix}{FolderNames.Objects}"),
                GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Constants.Mets, OriginalName = FolderNames.Metadata 
                    }, 
                    $"{Constants.AdmIdPrefix}{FolderNames.Metadata}", $"{Constants.TechIdPrefix}{FolderNames.Metadata}")
            }
            // NB we don't have a structLink because we have no logical structMap (yet)
        };
        
        return mets;
    }
    
    private static AmdSecType GetAmdSecType(FileFormatMetadata premisFile, string admId, string techId, string? digiprovId = null, VirusScanMetadata? virusScanMetadata = null, ExifMetadata? exifMetadata = null)
    {
        //TODO: use ProcessFileFormatDataForFile()
        var premis = PremisManager.Create(premisFile, exifMetadata);
        var xElement = PremisManager.GetXmlElement(premis, true);

        //TODO: this is a new amdsec
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

        var digiProvMd = PremisEventManager.Create(virusScanMetadata);
        var xVirusElement = PremisEventManager.GetXmlElement(digiProvMd);

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


    public List<string> GetRootAccessRestrictions(FullMets fullMets)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        return mods == null ? [] : mods.GetAccessConditions(Constants.RestrictionOnAccess); // may add Goobi things to this
    }

    public void SetRootAccessRestrictions(FullMets fullMets, List<string> accessRestrictions)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        if (mods is null) return;
        
        mods.RemoveAccessConditions(Constants.RestrictionOnAccess);
        foreach (var accessRestriction in accessRestrictions)
        {
            mods.AddAccessCondition(accessRestriction, Constants.RestrictionOnAccess);
        }
        ModsManager.SetRootMods(fullMets.Mets, mods);
    }

    public void SetRootRightsStatement(FullMets fullMets, Uri? uri)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        if (mods is null) return;
        
        mods.RemoveAccessConditions(Constants.UseAndReproduction);
        if (uri is not null)
        {
            mods.AddAccessCondition(uri.ToString(), Constants.UseAndReproduction);
        }
        ModsManager.SetRootMods(fullMets.Mets, mods);
    }
    
    public Uri? GetRootRightsStatement(FullMets fullMets)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        var rights = mods?.GetAccessConditions(Constants.UseAndReproduction).SingleOrDefault();
        return rights is not null ? new Uri(rights) : null;
    }

    //==============================================================
    //construct design with method stubs
    private Result ProcessFileFormatDataForFile(WorkingFile workingFile, string operationPath, bool newUpload)
    {
        //TODO: this will process new/existing metadata structure for new or existing file
        //Call IPremisManager to get XML
        //FileFormatMetadata premisFile;

        try
        {
            PremisFile = GetFileFormatMetadata(workingFile, operationPath);
        }
        catch (MetadataException mex)
        {
            //TODO: return a Result
            //return;
            return Result.Fail(ErrorCodes.BadRequest, mex.Message);
        }

        var patchPremisExif = workingFile.GetExifMetadata();

        PremisComplexType? premisType;
        if (PremisIncExifXml is not null)
        {
            premisType = PremisIncExifXml.GetPremisComplexType()!;
            PremisManager.Patch(premisType, PremisFile, patchPremisExif);
        }
        else
        {
            premisType = PremisManager.Create(PremisFile, patchPremisExif);
        }

        var premisXml = PremisManager.GetXmlElement(premisType, true);
        SetAmdSec(premisXml, newUpload);

        if (PremisFile.ContentType.HasText() && PremisFile.ContentType != ContentTypes.NotIdentified && File != null)
        {
            File.Mimetype = PremisFile.ContentType;
        }

        return Result.Ok();
    }

    //TODO: make these properties as used in all the methods
    //TODO: WorkingFile workingFile, string operationPath, AmdSecType amdSec, FileType file, string? fileAdmId
    private void ProcessVirusDataForFile(WorkingFile workingFile)
    {
        var patchPremisVirus = workingFile.GetVirusScanMetadata();

        EventComplexType? virusEventComplexType = null;
        if (VirusXml is not null)
        {
            virusEventComplexType = VirusXml.GetEventComplexType()!;

            if (patchPremisVirus != null)
            {
                PremisEventManager.Patch(virusEventComplexType, patchPremisVirus);
            }
        }
        else
        {
            if (patchPremisVirus != null)
            {
                virusEventComplexType = PremisEventManager.Create(patchPremisVirus);
            }
        }

        if (virusEventComplexType is null) return;
        VirusXml = PremisEventManager.GetXmlElement(virusEventComplexType);

        if (AmdSec == null) return;

        AddVirusXml();
    }


    //TODO: return Tuple of all metadata Premis, Virus and Exif (these may be null if a new file)
    private Result GetMetadataXml(FullMets fullMets, DivType? div, string operationPath)
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
        var premisIncExifXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); //TODO: this includes exif - separate this out
        var virusPremisXml = amdSec.DigiprovMd.FirstOrDefault(x => x.Id.Contains(Constants.VirusProvEventPrefix))?.MdWrap.XmlData.Any?.FirstOrDefault();

        PremisIncExifXml = premisIncExifXml;
        VirusXml = virusPremisXml;

        return Result.Ok();
    }

    private Result DeleteFile(DivType? div, FullMets fullMets, DivType? parent, string? operationPath)
    {
        // we have arrived at an existing file or folder which is being DELETED
        if (div != null && div.Div.Count > 0)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Cannot delete a non-empty directory.");
        }

        string? admId;
        if (div is { Type: "Item" })
        {
            SetFileAndFileGroup(div, fullMets);

            if (File != null && File.FLocat[0].Href != operationPath)
            {
                return Result.Fail(ErrorCodes.BadRequest, "Delete path doesn't match METS flocat");
            }

            admId = File != null && File.Admid.Count > 1 ? string.Join(" ", File.Admid) : File?.Admid[0];

            FileGroup?.File.Remove(File);
        }
        else
        {
            admId = div != null && div.Admid.Count > 1 ? string.Join(" ", div.Admid) : div.Admid[0];
        }

        // for both Files and Directories
        var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == admId);
        fullMets.Mets.AmdSec.Remove(amdSec);
        parent!.Div.Remove(div);

        return Result.Ok();
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

    private void SetFileAndFileGroup(DivType? div, FullMets fullMets)
    {
        if (div == null) return;
        var fileId = div.Fptr[0].Fileid;
        FileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        File = FileGroup.File.Single(f => f.Id == fileId);
    }

        
    private (int counter, DivType? parent, DivType? div, string[]? elements, string operationPath, string testPath) GetMetsElements(WorkingBase? workingBase, string? deletePath, FullMets fullMets)
    {
        //TODO: put in separate method this
        var operationPath = FolderNames.RemovePathPrefix(workingBase?.LocalPath ?? deletePath);
        var elements = operationPath!.Split('/', StringSplitOptions.RemoveEmptyEntries);

        //TODO: put this in a separate method
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

        return (counter, parent, div, elements, operationPath, testPath);
    }
    //TODO: need to reset properties when on new file
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