using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Extensions;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using Checksum = DigitalPreservation.Utils.Checksum;
using File = System.IO.File;

namespace Storage.Repository.Common.Mets;

public class MetsManager(
    IMetsParser metsParser, 
    IAmazonS3 s3Client) : IMetsManager
{
    private const string PhysIdPrefix = "PHYS_";
    private const string FileIdPrefix = "FILE_";
    private const string AdmIdPrefix = "ADM_";
    private const string TechIdPrefix = "TECH_";
    private const string DmdPhysRoot = "DMD_PHYS_ROOT";
    private const string ObjectsDivId = PhysIdPrefix + FolderNames.Objects;
    private const string MetadataDivId = PhysIdPrefix + FolderNames.Metadata;
    private const string DirectoryType = "Directory";
    private const string ItemType = "Item";

    public const string Mets = "METS";
    
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
        
        AddResourceToMets(mets, archivalGroup, mets.StructMap[0].Div, archivalGroup);
        
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
    /// <param name="archivalGroup"></param>
    /// <param name="div"></param>
    /// <param name="container"></param>
    private void AddResourceToMets(DigitalPreservation.XmlGen.Mets.Mets mets, ArchivalGroup archivalGroup, DivType div, Container container)
    {
        var agString = archivalGroup.Id!.ToString();
        foreach (var childContainer in container.Containers)
        {
            DivType? childDirectoryDiv = null;
            if (container is ArchivalGroup && childContainer.GetSlug() == FolderNames.Objects)
            {
                // The objects div should already exist from our template
                childDirectoryDiv = mets.StructMap[0].Div.Div.Single(d => d.Id == ObjectsDivId);
            }

            if (childDirectoryDiv == null)
            {
                var localPath = childContainer.Id!.ToString().RemoveStart(agString).RemoveStart("/");
                var admId = AdmIdPrefix + localPath;
                var techId = TechIdPrefix + localPath;
                childDirectoryDiv = new DivType
                {
                    Type = DirectoryType,
                    Label = childContainer.Name,
                    Id = $"{PhysIdPrefix}{localPath}",
                    Admid = { admId }
                };
                div.Div.Add(childDirectoryDiv);
                var reducedPremisForObjectDir = new PremisMetadata
                {
                    Source = Mets,
                    OriginalName = localPath
                };
                mets.AmdSec.Add(GetAmdSecType(reducedPremisForObjectDir, admId, techId));
            }
            AddResourceToMets(mets, archivalGroup, childDirectoryDiv, childContainer);
        }

        foreach (var binary in container.Binaries)
        {
            var localPath = binary.Id!.ToString().RemoveStart(agString).RemoveStart("/");
            if (IsMetsFile(localPath!))
            {
                continue;
            }
            var fileId = FileIdPrefix + localPath;
            var admId = AdmIdPrefix + localPath;
            var techId = TechIdPrefix + localPath;
            var childItemDiv = new DivType
            {
                Type = ItemType,
                Label = binary.Name,
                Id = $"{PhysIdPrefix}{localPath}",
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
            var premisFile = new PremisMetadata
            {
                Source = Mets,
                Digest = binary.Digest,
                Size = binary.Size,
                OriginalName = localPath
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
        var rootDmd = mets.DmdSec.Single(x => x.Id == DmdPhysRoot)!;
        rootDmd.MdWrap = new MdSecTypeMdWrap{ Mdtype = MdSecTypeMdWrapMdtype.Mods };
        var mods = $"""<mods:mods xmlns:mods="http://www.loc.gov/mods/v3"><mods:name>{agNameFromDeposit ?? "[Untitled]"}</mods:name></mods:mods>""";
        rootDmd.MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { GetElement(mods) } };
        return (file, mets);
    }


    
    public async Task<Result> WriteMets(FullMets fullMets)
    {
        // TODO - re-use serializer? re-use XmlSerializerNamespaces GetNamespaces()?
        
        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
        var sb = new StringBuilder();
        var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
            Encoding = Encoding.UTF8,
            Indent = true,
        });
        serializer.Serialize(writer, fullMets.Mets, GetNamespaces());
        writer.Close();
        var xml = sb.ToString();

        switch (fullMets.Uri.Scheme)
        {
            case "file":
                await File.WriteAllTextAsync(fullMets.Uri.LocalPath, xml);
                return Result.Ok();
            
            case "s3":
                var awsUri = new AmazonS3Uri(fullMets.Uri);
                var req = new PutObjectRequest
                {
                    BucketName = awsUri.Bucket,
                    Key = awsUri.Key,
                    ContentType = "application/xml",
                    ContentBody = xml,
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                if (fullMets.ETag != null)
                {
                    req.IfMatch = fullMets.ETag;
                }
                var resp = await s3Client.PutObjectAsync(req);
                if (resp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
                {
                    return Result.Ok();
                }
                if (resp.HttpStatusCode is HttpStatusCode.PreconditionFailed)
                {
                    return Result.Fail(ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
                }
                return Result.Fail(ErrorCodes.BadRequest, "AWS returned HTTP Status " + resp.HttpStatusCode + " when writing METS");
            
            default:
                return Result.Fail(ErrorCodes.BadRequest, fullMets.Uri.Scheme + " not supported");
        }
    }

    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
        DigitalPreservation.XmlGen.Mets.Mets? mets = null; 
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (_, file) = fileLocResult.Value;
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation);
        }
        
        string? returnedETag;

        switch (file.Scheme)
        {
            case "s3":
                var s3Uri = new AmazonS3Uri(file);
                var gor = new GetObjectRequest
                {
                    BucketName = s3Uri.Bucket,
                    Key = s3Uri.Key,
                };
                if (eTagToMatch is not null)
                {
                    gor.EtagToMatch = eTagToMatch;
                }
                try
                {
                    var resp = await s3Client.GetObjectAsync(gor);
                    if (resp.HttpStatusCode == HttpStatusCode.OK)
                    {
                        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
                        using var reader = XmlReader.Create(resp.ResponseStream);
                        mets = (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;
                    }
                    if (resp.HttpStatusCode is HttpStatusCode.PreconditionFailed)
                    {
                        return Result.FailNotNull<FullMets>(ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
                    }
                    returnedETag = resp.ETag;
                }
                catch (Exception e)
                {
                    return Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, e.Message);
                }
                break;
            
            case "file":
                var fi = new FileInfo(file.LocalPath);
                try
                {
                    returnedETag = Checksum.Sha256FromFile(fi);
                    if (eTagToMatch is not null && returnedETag != eTagToMatch)
                    {
                        return Result.FailNotNull<FullMets>(
                            ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
                    }
                    var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
                    using var reader = XmlReader.Create(file.LocalPath);
                    mets = (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;
                }
                catch (Exception e)
                {
                    return Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, e.Message);
                }
                break;
            
            default:
                return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest, file.Scheme + " not supported");
        }


        string? agentName = null;
        if (mets?.MetsHdr?.Agent is not null && mets.MetsHdr.Agent.Count > 0)
        {
            agentName = mets.MetsHdr.Agent[0].Name;
        }

        if (agentName != IMetsManager.MetsCreatorAgent)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest, "METS file was not created by " + IMetsManager.MetsCreatorAgent);
        }

        if (mets != null)
        {
            var fullMetal = new FullMets
            {
                Mets = mets,
                Uri = file,
                ETag = returnedETag
            };
            return Result.OkNotNull(fullMetal);
        }

        return Result.FailNotNull<FullMets>(
            ErrorCodes.UnknownError, "Unable to read METS");
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

    private static Result EditMets(WorkingBase? workingBase, string? deletePath, FullMets fullMets)
    {
        // Add workingBase to METS
        // This is where our non-opaque phys structmap IDs come into play.
        // This may not be a good idea. But without it, we need a more complex way of
        // finding the METS parts, like the XDocument parsing in MetsParser.

        var operationPath = FolderNames.RemovePathPrefix(workingBase?.LocalPath ?? deletePath);
        var elements = operationPath!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var div = fullMets.Mets.StructMap.Single(sm => sm.Type=="PHYSICAL").Div!;
        DivType? parent = null;
        var testPath = string.Empty;
        int counter = 0;
        foreach (var element in elements)
        {
            if (testPath.HasText())
            {
                testPath += "/";
            }
            testPath += element;
            // This is navigating using our ID convention for directories
            // If we don't want to do this, we can use the premis:originalName for the directory
            var childDiv = div.Div.SingleOrDefault(d => d.Id == $"{PhysIdPrefix}{testPath}");
            if (childDiv is null)
            {
                break;
            }

            counter++;
            parent = div;
            div = childDiv;
        }
            
        // div might be the file itself, or a parent or grandparent directory.
        // But for this atomic upload file handler we will not allow any Directory creation, that
        // must have already happened in HandleCreateFolder
        // i.e., we must already be at the last or penultimate member of elements
        if (counter == elements.Length)
        {
            if (deletePath is not null)
            {
                if (workingBase is not null)
                {
                    return Result.Fail(ErrorCodes.BadRequest, "Cannot supply a WorkingBase and a deletePath.");
                }
                    
                // we have arrived at an existing file or folder which is being DELETED
                if (div.Div.Count > 0)
                {
                    return Result.Fail(ErrorCodes.BadRequest, "Cannot delete a non-empty directory.");
                }

                string admId;
                if (div.Type == "Item")
                {
                    // for Files only
                    var fileId = div.Fptr[0].Fileid;
                    var fileGrp = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
                    var file = fileGrp.File.Single(f => f.Id == fileId);
                    if (file.FLocat[0].Href != operationPath)
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "Delete path doesn't match METS flocat");
                    }

                    admId = file.Admid[0];
                    fileGrp.File.Remove(file);
                }
                else
                {
                    admId = div.Admid[0];
                }
                    
                // for both Files and Directories
                var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == admId);
                fullMets.Mets.AmdSec.Remove(amdSec);
                parent!.Div.Remove(div);
            }
            else
            {
                // we have arrived at an existing file or folder which is being OVERWRITTEN
                if (workingBase is WorkingDirectory && div.Type != "Directory")
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");
                }

                if (workingBase is WorkingFile workingFile)
                {
                    if (div.Type != "Item")
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");
                    }
                    var fileId = div.Fptr[0].Fileid;
                    var fileGrp = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
                    var file = fileGrp.File.Single(f => f.Id == fileId);
                    if (file.FLocat[0].Href != operationPath)
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");
                    }

                    var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == file.Admid[0]);
                    var premisXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault();
                    var patchPremis = new PremisMetadata
                    {
                        Source = Mets,
                        Digest = workingFile.Digest,
                        Size = workingFile.Size,
                        OriginalName = operationPath // workingFile.LocalPath
                    };  
                    PremisComplexType? premisType;
                    if (premisXml is not null)
                    {
                        premisType = premisXml.GetPremisComplexType()!;
                        PremisManager.Patch(premisType, patchPremis);
                    }
                    else
                    {
                        premisType = PremisManager.Create(patchPremis);
                    }
                    premisXml = PremisManager.GetXmlElement(premisType, true);
                    amdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { premisXml } };
                }
                else if (workingBase is WorkingDirectory workingDirectory)
                {
                    if(div.Type != "Directory")
                    {
                        return Result.Fail(ErrorCodes.BadRequest, "WorkingDirectory path does not end on a directory");
                    }

                    // Is there anything else that could be done here?
                    if (workingDirectory.Name.HasText())
                    {
                        // and is it even done on hasText?
                        div.Label = workingDirectory.Name;
                    }
                }
                else
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
                }                    
            }
        } 
        else if (counter == elements.Length - 1)
        {
            if (deletePath is not null)
            {
                return Result.Fail(ErrorCodes.NotFound, "Can't find a file or folder to delete.");
            }
            // div is a directory
            if (div.Type != "Directory")
            {
                return Result.Fail(ErrorCodes.BadRequest, "Parent path is not a Directory");
            }

            if (workingBase is null)
            {
                return Result.Fail(ErrorCodes.BadRequest, "No working directory or working file supplied to add.");
            }

            var physId = PhysIdPrefix + operationPath;
            var admId = AdmIdPrefix + operationPath;
            var techId = TechIdPrefix + operationPath;
                
            if (workingBase is WorkingFile workingFile)
            {
                var fileId = FileIdPrefix + operationPath;
                var childItemDiv = new DivType
                {
                    Type = ItemType,
                    Label = workingFile.Name ?? operationPath.GetSlug(),
                    Id = physId,
                    Fptr = { new DivTypeFptr{ Fileid = fileId } }
                };
                div.Div.Add(childItemDiv);
                fullMets.Mets.FileSec.FileGrp[0].File.Add(
                    new FileType
                    {
                        Id = fileId, 
                        Admid = { admId },
                        Mimetype = workingFile.ContentType,
                        FLocat = { 
                            new FileTypeFLocat
                            {
                                Href = operationPath, Loctype = FileTypeFLocatLoctype.Url
                            } 
                        }
                    });
                var premisFile = new PremisMetadata
                {
                    Source = Mets,
                    Digest = workingFile.Digest,
                    Size = workingFile.Size,
                    OriginalName = operationPath // workingFile.LocalPath
                };
                fullMets.Mets.AmdSec.Add(GetAmdSecType(premisFile, admId, techId));
            }
            else if (workingBase is WorkingDirectory workingDirectory)
            {
                var childDirectoryDiv = new DivType
                {
                    Type = DirectoryType,
                    Label = workingDirectory.Name ?? operationPath.GetSlug(),
                    Id = physId,
                    Admid = { admId }
                };
                div.Div.Add(childDirectoryDiv);
                var premisFile = new PremisMetadata
                {
                    Source = Mets,
                    OriginalName = operationPath // workingDirectory.LocalPath
                };
                fullMets.Mets.AmdSec.Add(GetAmdSecType(premisFile, admId, techId));
            }
            else
            {
                return Result.Fail(ErrorCodes.BadRequest, "WorkingBase is unsupported type");
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
            return Result.Fail(ErrorCodes.BadRequest, "Cannot upload a file into a nonexistent directory");
        }

        return Result.Ok();
    }


    public bool IsMetsFile(string fileName)
    {
        return MetsUtils.IsMetsFile(fileName);
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
                        Name = IMetsManager.MetsCreatorAgent
                    }
                }
            },
            DmdSec =
            {
                new MdSecType { Id = DmdPhysRoot }
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
                        Type = DirectoryType,  
                        Dmdid = { DmdPhysRoot },
                        Div = { 
                            new DivType
                            {
                                Id = MetadataDivId,  // do this with premis:originalName metadata for directories?
                                Type = DirectoryType,
                                Label = FolderNames.Metadata,
                                Dmdid = { $"DMD_{FolderNames.Metadata}" },
                                Admid = { $"ADM_{FolderNames.Metadata}" }
                            }, 
                            new DivType
                            {
                                Id = ObjectsDivId,  // do this with premis:originalName metadata for directories?
                                Type = DirectoryType,
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
                GetAmdSecType(new PremisMetadata
                    {
                        Source = Mets, OriginalName = FolderNames.Objects 
                    }, 
                    $"{AdmIdPrefix}{FolderNames.Objects}", $"{TechIdPrefix}{FolderNames.Objects}"),
                GetAmdSecType(new PremisMetadata
                    {
                        Source = Mets, OriginalName = FolderNames.Metadata 
                    }, 
                    $"{AdmIdPrefix}{FolderNames.Metadata}", $"{TechIdPrefix}{FolderNames.Metadata}")
            }
            // NB we don't have a structLink because we have no logical structMap (yet)
        };
        
        return mets;
    }
    
    private static XmlSerializerNamespaces GetNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add("mets", "http://www.loc.gov/METS/");
        ns.Add("mods", "http://www.loc.gov/mods/v3");
        ns.Add("premis", "http://www.loc.gov/premis/v3");
        ns.Add("xlink", "http://www.w3.org/1999/xlink");
        ns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        return ns;
    }
    
    
    private static AmdSecType GetAmdSecType(PremisMetadata premisFile, string admId, string techId)
    {
        var premis = PremisManager.Create(premisFile);
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
            }
        };
        return amdSec;
    }
    
    [Obsolete("Make a ModsManager like PremisManager")]
    private static XmlElement GetElement(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement!;
    }
}