using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;

namespace Storage.Repository.Common.Mets;

public class MetsManager(
    IMetsParser metsParser, 
    IAmazonS3 s3Client) : IMetsManager
{
    private const string PhysIdPrefix = "PHYS_";
    private const string DmdPhysRoot = "DMD_PHYS_ROOT";
    private const string DmdObjects = "DMD_OBJECTS";
    private const string ObjectsDivId = "PHYS_objects";
    private const string DirectoryType = "Directory";
    private const string ItemType = "Item";
    
    public async Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, string? agNameFromDeposit)
    {
        var (file, mets) = GetStandardMets(metsLocation, agNameFromDeposit);
        var writeResult = await WriteMets(new FullMets{ Mets = mets, Uri = file });
        if (writeResult.Success)
        {
            return await metsParser.GetMetsFileWrapper(file);
        }
        return Result.FailNotNull<MetsFileWrapper>(writeResult.ErrorCode!, writeResult.ErrorMessage);
    }

    public async Task<Result<MetsFileWrapper>> CreateStandardMets(Uri metsLocation, ArchivalGroup archivalGroup, string? agNameFromDeposit)
    {
        var (file, mets) = GetStandardMets(metsLocation, agNameFromDeposit);
        
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
        var agString = archivalGroup.Id.ToString();
        foreach (var childContainer in container.Containers)
        {
            DivType? childDirectoryDiv = null;
            if (container is ArchivalGroup && childContainer.GetSlug() == "objects")
            {
                // The objects div should already exist from our template
                childDirectoryDiv = mets.StructMap[0].Div.Div.Single(d => d.Id == ObjectsDivId);
            }

            if (childDirectoryDiv == null)
            {
                var localPath = childContainer.Id!.ToString().RemoveStart(agString).RemoveStart("/");
                childDirectoryDiv = new DivType
                {
                    Type = DirectoryType,
                    Label = childContainer.Name,
                    Id = $"{PhysIdPrefix}{localPath}"
                };
                div.Div.Add(childDirectoryDiv);
            }
            AddResourceToMets(mets, archivalGroup, childDirectoryDiv, childContainer);
        }

        foreach (var binary in container.Binaries)
        {
            var localPath = binary.Id!.ToString().RemoveStart(agString).RemoveStart("/");
            var fileId = "FILE_" + localPath;
            var admId = "ADM_" + localPath;
            var techMdId = "TECH_" + localPath;
            var reducedPremis = PremisFixityWrapper
                .Replace("{sha256}", binary.Digest)
                .Replace("{localPath}", localPath);
            var reducedPremisX = GetElement(reducedPremis);
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
            var amdSec = new AmdSecType
            {
                Id = admId,
                TechMd =
                {
                    new MdSecType
                    {
                        Id = techMdId,
                        MdWrap = new MdSecTypeMdWrap
                        {
                            Mdtype = MdSecTypeMdWrapMdtype.PremisObject,
                            XmlData = new MdSecTypeMdWrapXmlData { Any = { reducedPremisX } }
                        }
                    }
                }
            };
            mets.AmdSec.Add(amdSec);
        }
    }

    private (Uri file, DigitalPreservation.XmlGen.Mets.Mets mets) GetStandardMets(Uri metsLocation, string? agNameFromDeposit)
    {
        // might be a file path or an S3 URI
        var (root, file) = MetsUtils.GetRootAndFile(metsLocation);
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


    
    private async Task<Result> WriteMets(FullMets fullMets)
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
        var (_, file) = MetsUtils.GetRootAndFile(metsLocation);
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation);
        }
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
        string? returnedETag = null;
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
        var result = await GetFullMets(workingRoot, depositETag);
        if (result.Success)
        {
            var fullMets = result.Value!;
            
            // Add workingFile to METS
            // This is where our non-opaque phys structmap IDs come into play.
            // This may not be a good idea. But without it we need a more complex way of
            // finding the METS parts, like the XDocument parsing in MetsParser.

            var elements = workingFile.LocalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var div = fullMets.Mets.StructMap.Single(sm => sm.Type=="PHYSICAL").Div!;
            var testPath = string.Empty;
            int counter = 0;
            foreach (var element in elements)
            {
                counter++;
                if (testPath.HasText())
                {
                    testPath += "/";
                }
                testPath += element;
                var childDiv = div.Div.SingleOrDefault(d => d.Id == $"{PhysIdPrefix}{testPath}");
                if (childDiv is null)
                {
                    break;
                }

                div = childDiv;
            }
            
            // div might be the file itself, or a parent or grandparent directory.
            // But for this atomic upload file handler we will not allow any Directory creation, that
            // must have already happened in HandleCreateFolder
            // i.e., we must already be at the last or penultimate member of elements
            if (counter == elements.Length)
            {
                // we have arrived at an existing file which is being overwritten
                if (div.Type != "Item")
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path does not end on a file");
                }

                var fileId = div.Fptr[0].Fileid;
                var fileGrp = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
                var file = fileGrp.File.Single(f => f.Id == fileId);
                if (file.FLocat[0].Href != workingFile.LocalPath)
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingFile path doesn't match METS flocat");
                }

                var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == file.Admid[0]);
                
                // This replaces the Premis metadata, which won't be OK later when it gets richer
                // from pipeline decorators.
                // TODO: Instead, need to merge the premis metadata.
                var reducedPremis = PremisFixityWrapper
                    .Replace("{sha256}", workingFile.Digest)
                    .Replace("{localPath}", workingFile.LocalPath);
                var reducedPremisX = GetElement(reducedPremis);
                amdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData { Any = { reducedPremisX } };
            } 
            else if (counter == elements.Length - 1)
            {
                // This must be a directory
                if (div.Type != "Directory")
                {
                    return Result.Fail(ErrorCodes.BadRequest, "WorkingFile parent path is not a Directory");
                }
                
                // create the Item, fptr, file element etc
                
                // (get dir creation working too then test)
            }
            else
            {
                return Result.Fail(ErrorCodes.BadRequest, "Cannot upload a file into a nonexistent directory");
            }
            
            await WriteMets(fullMets!);
            return Result.Ok();
        }
        return Result.Fail(result.ErrorCode ?? ErrorCodes.UnknownError, result.ErrorMessage);
    }

    public async Task<Result> HandleDeleteObject(Uri workingRoot, string localPath, string depositETag)
    {
        var result = await GetFullMets(workingRoot, depositETag);
        if (result.Success)
        {
            var fullMets = result.Value;
            
            // delete localPath from METS
            
            await WriteMets(fullMets!);
            return Result.Ok();
        }
        return Result.Fail(result.ErrorCode ?? ErrorCodes.UnknownError, result.ErrorMessage);
    }

    public async Task<Result> HandleCreateFolder(Uri workingRoot, WorkingDirectory workingDirectory, string depositETag)
    {
        var result = await GetFullMets(workingRoot, depositETag);
        if (result.Success)
        {
            var fullMets = result.Value;
            
            // create new directory in METS
            
            await WriteMets(fullMets!);
            return Result.Ok();
        }
        return Result.Fail(result.ErrorCode ?? ErrorCodes.UnknownError, result.ErrorMessage);
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
            AmdSec = {  },
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
                                Id = ObjectsDivId,  // do this with premis:originalName metadata for directories?
                                Type = DirectoryType,
                                Label = "objects",
                                Dmdid = { DmdObjects },
                            } 
                        }
                    }
                }
            }
            // NB we don't have a structLink because we have no logical structMap (yet)
        };
        
        return mets;
    }
    const string PremisFixityWrapper = """
                                       <premis:object xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:premis="http://www.loc.gov/premis/v3" xsi:type="premis:file" xsi:schemaLocation="http://www.loc.gov/premis/v3 http://www.loc.gov/standards/premis/v3/premis.xsd" version="3.0">
                                           <premis:objectCharacteristics>
                                               <premis:fixity>
                                                   <premis:messageDigestAlgorithm>sha256</premis:messageDigestAlgorithm>
                                                   <premis:messageDigest>{sha256}</premis:messageDigest>
                                               </premis:fixity>
                                           </premis:objectCharacteristics>
                                           <premis:originalName>{localPath}</premis:originalName>
                                       </premis:object>
                                       """;
    
    private static XmlElement GetElement(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement!;
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
}