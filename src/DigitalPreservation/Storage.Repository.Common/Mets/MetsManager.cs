using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Amazon.Runtime.Internal.Util;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Extensions;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using Microsoft.Extensions.Logging;
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
    public const string DmdPhysRoot = "DMD_PHYS_ROOT";
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
                childDirectoryDiv = mets.StructMap[0].Div.Div.Single(d => d.Id == ObjectsDivId);
            }

            if (childDirectoryDiv == null)
            {
                var localPath = childContainer.Id!.LocalPath.RemoveStart(agLocalPath).RemoveStart("/");
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
                var reducedPremisForObjectDir = new FileFormatMetadata
                {
                    Source = Mets,
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
            var premisFile = new FileFormatMetadata
            {
                Source = Mets,
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

    private Result EditMets(WorkingBase? workingBase, string? deletePath, FullMets fullMets)
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

                    // TODO: This is a quick fix to get round the problem of spaces in XML IDs.
                    // We need to not have any spaces in XML IDs, which means we need to escape them 
                    // in a reversible way (replacing with _ won't do)
                    var fileAdmId = string.Join(' ', file.Admid);
                    var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == fileAdmId);
                    var premisXml = amdSec.TechMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault();
                    var virusPremisXml = amdSec.DigiprovMd.FirstOrDefault()?.MdWrap.XmlData.Any?.FirstOrDefault(); 

                    FileFormatMetadata patchPremis;
                    VirusScanMetadata? patchPremisVirus;

                    try
                    {
                        patchPremis = GetFileFormatMetadata(workingFile, operationPath);
                    }
                    catch (MetadataException mex)
                    {
                        return Result.Fail(ErrorCodes.BadRequest, mex.Message);
                    }

                    try
                    {
                        patchPremisVirus = GetVirusScanMetadata(workingFile, operationPath);
                    }
                    catch (MetadataException mex)
                    {
                        return Result.Fail(ErrorCodes.BadRequest, mex.Message);
                    }

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

                    if (patchPremis.ContentType.HasText() && patchPremis.ContentType != ContentTypes.NotIdentified)
                    {
                        file.Mimetype = patchPremis.ContentType;
                    }

                    //TODO: var xVirusElement = PremisEventManager.GetXmlElement(digiProvMd);

                    //-------------------------------------------------------------------------------------
                    //------------------------------------------------------------------------------
                    EventComplexType? virusEventComplexType = null;
                    if (virusPremisXml is not null)
                    {
                        virusEventComplexType = virusPremisXml.GetEventComplexType()!; //GETEVENTCOmplexType
                        PremisEventManager.Patch(virusEventComplexType, patchPremisVirus); //Need to patch teh complex type
                    }
                    else
                    {
                        if (patchPremisVirus != null)
                        {
                            virusEventComplexType = PremisEventManager.Create(patchPremisVirus);
                        }

                    }

                    if (virusEventComplexType is not null)
                    {
                        virusPremisXml = PremisEventManager.GetXmlElement(virusEventComplexType); //, true
                        //amdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { virusPremisXml } };

                        if (amdSec.DigiprovMd.Any())
                        {
                            amdSec.DigiprovMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { virusPremisXml } };
                        }
                        else
                        {
                            amdSec.DigiprovMd.Add(new MdSecType
                            {
                                Id = "digiprovMD_ClamAV", //digiprovId
                                MdWrap = new MdSecTypeMdWrap
                                {
                                    Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                                    XmlData = new MdSecTypeMdWrapXmlData { Any = { virusPremisXml } }
                                }
                            });
                        }
                    }
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
                FileFormatMetadata premisFile;
                VirusScanMetadata? virusScanMetadata;
                try
                {
                    premisFile = GetFileFormatMetadata(workingFile, operationPath);
                }
                catch (MetadataException mex)
                {
                    return Result.Fail(ErrorCodes.BadRequest, mex.Message);
                }


                try
                {
                    virusScanMetadata = GetVirusScanMetadata(workingFile, operationPath);
                }
                catch (MetadataException mex)
                {
                    return Result.Fail(ErrorCodes.BadRequest, mex.Message);
                }


                fullMets.Mets.FileSec.FileGrp[0].File.Add(
                    new FileType
                    {
                        Id = fileId, 
                        Admid = { admId },
                        Mimetype = premisFile.ContentType ?? workingFile.ContentType,
                        FLocat = { 
                            new FileTypeFLocat
                            {
                                Href = operationPath, Loctype = FileTypeFLocatLoctype.Url
                            } 
                        }
                    });
                fullMets.Mets.AmdSec.Add(GetAmdSecType(premisFile, admId, techId, "digiprovMD_ClamAV", virusScanMetadata));
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
                var premisFile = new FileFormatMetadata
                {
                    Source = Mets,
                    OriginalName = operationPath, // workingDirectory.LocalPath
                    StorageLocation = null // storageLocation
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
            // if (fileFormatMetadata.StorageLocation == null)
            // {
            //     fileFormatMetadata.StorageLocation = storageLocation;
            // }
            return fileFormatMetadata;
        }
        
        // no metadata available
        return new FileFormatMetadata
        {
            Source = Mets,
            ContentType = workingFile.ContentType,
            Digest = digestMetadata?.Digest ?? workingFile.Digest,
            Size = workingFile.Size,
            OriginalName = originalName, // workingFile.LocalPath
            StorageLocation = null // storageLocation
        };
    }

    private static VirusScanMetadata? GetVirusScanMetadata(WorkingFile workingFile, string originalName)
    {
        // This will throw if mismatches
        //var digestMetadata = workingFile.GetDigestMetadata();

        var virusScanMetadata = workingFile.GetVirusScanMetadata();
        if (virusScanMetadata != null)
        {
            return virusScanMetadata;
        }

        //get from infected files and write


        return null;
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
                GetAmdSecType(new FileFormatMetadata
                    {
                        Source = Mets, OriginalName = FolderNames.Objects 
                    }, 
                    $"{AdmIdPrefix}{FolderNames.Objects}", $"{TechIdPrefix}{FolderNames.Objects}"),
                GetAmdSecType(new FileFormatMetadata
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
    
    
    private static AmdSecType GetAmdSecType(FileFormatMetadata premisFile, string admId, string techId, string? digiprovId = null, VirusScanMetadata? virusScanMetadata = null)
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
        return mods == null ? [] : mods.GetAccessConditions(IMetsManager.RestrictionOnAccess); // may add Goobi things to this
    }

    public void SetRootAccessRestrictions(FullMets fullMets, List<string> accessRestrictions)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        if (mods is null) return;
        
        mods.RemoveAccessConditions(IMetsManager.RestrictionOnAccess);
        foreach (var accessRestriction in accessRestrictions)
        {
            mods.AddAccessCondition(accessRestriction, IMetsManager.RestrictionOnAccess);
        }
        ModsManager.SetRootMods(fullMets.Mets, mods);
    }

    public void SetRootRightsStatement(FullMets fullMets, Uri? uri)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        if (mods is null) return;
        
        mods.RemoveAccessConditions(IMetsManager.UseAndReproduction);
        if (uri is not null)
        {
            mods.AddAccessCondition(uri.ToString(), IMetsManager.UseAndReproduction);
        }
        ModsManager.SetRootMods(fullMets.Mets, mods);
    }
    
    public Uri? GetRootRightsStatement(FullMets fullMets)
    {
        var mods = ModsManager.GetRootMods(fullMets.Mets);
        var rights = mods?.GetAccessConditions(IMetsManager.UseAndReproduction).SingleOrDefault();
        return rights is not null ? new Uri(rights) : null;
    }
}