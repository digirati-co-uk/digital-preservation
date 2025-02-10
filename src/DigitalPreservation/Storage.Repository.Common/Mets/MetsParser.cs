using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Storage.Repository.Common.Mets;

public class MetsParser(
    IAmazonS3 s3Client,
    ILogger<MetsParser> logger) : IMetsParser
{
    public async Task<Result<MetsFileWrapper>> GetMetsFileWrapper(Uri metsLocation, bool parse = true)
    {
        // might be a file path or an S3 URI
        var (root, file) = MetsUtils.GetRootAndFile(metsLocation);
        var mets = new MetsFileWrapper
        {
            Root = root,
            Self = await FindMetsFileAsync(root, file),
            PhysicalStructure = Storage.RootDirectory()
        };
        if(mets.Self != null)
        {
            var (xMets, eTag) = await ExamineXml(mets, parse);
            mets.ETag = eTag;
            if (parse)
            {
                PopulateFromMets(mets, xMets);
            }
            if (mets.PhysicalStructure.FindFile(mets.Self.LocalPath) is null)
            {
                // If the METS file doesn't include itself in the root
                mets.PhysicalStructure.Files.Add(mets.Self);
            }
            if (mets.Files.SingleOrDefault(f => f.LocalPath == mets.Self.LocalPath) is null)
            {
                // and in the flat list
                mets.Files.Add(mets.Self);
            }

            mets.XDocument = xMets;
        }
        return Result.OkNotNull(mets);
    }



    private async Task<WorkingFile?> FindMetsFileAsync(Uri root, Uri? file)
    {
        // This "find the METS file" logic is VERY basic and doesn't even look at the file.
        // But this is just for Proof of Concept.

        switch (root.Scheme)
        {
            case "file":
                var dir = new DirectoryInfo(root.AbsolutePath);
                if (file is not null)
                {
                    var fi = new FileInfo(file.LocalPath);
                    return GetWorkingFileFromFileInfo(fi);
                }
                
                // Need to find the METS
                var firstXmlFile = dir.EnumerateFiles().FirstOrDefault(f => MetsUtils.IsMetsFile(f.Name));
                if (firstXmlFile != null)
                {
                    return GetWorkingFileFromFileInfo(firstXmlFile);
                }

                break;
            case "s3":
                var rootS3Uri = new AmazonS3Uri(root);
                var prefix = $"{rootS3Uri.Key.TrimEnd('/')}/";
                if (file is not null)
                {
                    var fileS3Uri = new AmazonS3Uri(file);
                    return await GetWorkingFileFromS3Location(fileS3Uri.Bucket, fileS3Uri.Key, prefix);
                }
                
                // Need to find the METS
                var listObjectsReq = new ListObjectsV2Request
                {
                    BucketName = rootS3Uri.Bucket,
                    Prefix = prefix,
                    Delimiter = "/" // first "children" only                        
                };
                var resp = await s3Client.ListObjectsV2Async(listObjectsReq);
                var firstXmlKey = resp.S3Objects.FirstOrDefault(s => MetsUtils.IsMetsFile(s.Key));
                if (firstXmlKey != null)
                {
                    var bucket = firstXmlKey.BucketName;
                    var key = firstXmlKey.Key;
                    return await GetWorkingFileFromS3Location(bucket, key, prefix);
                }

                // 
                break;
            default:
                throw new NotSupportedException(root.Scheme + " not supported");
        }

        return null;

        WorkingFile? GetWorkingFileFromFileInfo(FileInfo fi)
        {
            return new WorkingFile
            {
                ContentType = "application/xml",
                LocalPath = fi.Name,
                Name = fi.Name,
                Digest = Checksum.Sha256FromFile(fi)
            };
        }

        async Task<WorkingFile?> GetWorkingFileFromS3Location(string bucket, string key, string prefix)
        {
            var s3Stream = await s3Client.GetObjectStreamAsync(bucket, key, null);
            var digest = Checksum.Sha256FromStream(s3Stream);
            var name = key.Replace(prefix, string.Empty);
            return new WorkingFile
            {
                ContentType = "application/xml",
                LocalPath = name,
                Name = name,
                Digest = digest
            };
        }
    }
    
    
    private async Task<(XDocument?, string)> ExamineXml(MetsFileWrapper mets, bool parse)
    {
        XDocument? xDoc = null;
        switch(mets.Root!.Scheme)
        {
            case "file":
                var fileETag = mets.Self.Digest!;
                if (parse)
                {
                    xDoc = XDocument.Load(mets.Root + mets.Self!.LocalPath);
                }
                return (xDoc, fileETag);

            case "s3":
                var s3Uri = new AmazonS3Uri(mets.Root + mets.Self!.LocalPath);
                var resp = await s3Client.GetObjectAsync(s3Uri.Bucket, s3Uri.Key);
                var s3ETag = resp.ETag!;
                if (parse)
                {
                    xDoc = await XDocument.LoadAsync(resp.ResponseStream, LoadOptions.None, CancellationToken.None);
                }
                return (xDoc, s3ETag);

            default:
                throw new NotSupportedException(mets.Root!.Scheme + " not supported");
        }
    }
    
    
        public void PopulateFromMets(MetsFileWrapper mets, XDocument xMets)
        {
            var modsTitle = xMets.Descendants(XNames.mods + "title").FirstOrDefault()?.Value;
            var modsName = xMets.Descendants(XNames.mods + "name").FirstOrDefault()?.Value;
            string? name = modsTitle ?? modsName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                mets.Name = name;
            }
            // TODO - where else to look for title?

            // There may be more than one, and they may or may not be qualified as physical or logical
            XElement? physicalStructMap = null;
            foreach (var sm in xMets.Descendants(XNames.MetsStructMap))
            {
                var typeAttr = sm.Attribute("TYPE");
                if (typeAttr?.Value != null)
                {
                    if (typeAttr.Value.ToLowerInvariant() == "physical")
                    {
                        physicalStructMap = sm;
                        break;
                    }
                    if (typeAttr.Value.ToLowerInvariant() == "logical")
                    {
                        continue;
                    }
                }
                if (physicalStructMap == null)
                {
                    // This may get overwritten if we find a better one in the loop
                    // EPRints METS files structMap don't have type
                    physicalStructMap = sm;
                }
            }

            if (physicalStructMap == null)
            {
                throw new NotSupportedException("METS file muct have a physical structmap");
            }

            // Now walk down the structmap
            // Each div either contains 1 (or sometimes more) mets:fptr, or it contains child DIVs.
            // If a DIV containing a mets:fptr has a LABEL (not ORDERLABEL) then that is the name of the file
            // If those DIVs have TYPE="Directory" and a LABEL, that gives us the name of the directory.
            // We need to see the path of the file, too.

            // A DIV TYPE="Directory" should never directly contain a file

            // GOOBI METS at Wellcome contain images and ALTO in the same DIV; the ADM_ID is for the Image not the ALTO.
            // Not sure how to be formal about that.

            var parent = physicalStructMap;
            var fileSec = xMets.Descendants(XNames.MetsFileSec).Single();

            // This relies on all directories having labels not just some
            Stack<string> directoryLabels = new();

            ProcessChildStructDivs(mets, xMets, parent, fileSec, directoryLabels);
            
            // We should now have a flat list of WorkingFile, and a set of WorkingDirectories, with correct names
            // if supplied. Now assign the files to their directories.

            foreach (var file in mets.Files)
            {
                var folder = mets.PhysicalStructure!.FindDirectory(file.LocalPath.GetParent(), false);
                if (folder is null)
                {
                    throw new Exception("Our folder logic is wrong");
                }
                folder.Files.Add(file);
            }

        }

        private void ProcessChildStructDivs(MetsFileWrapper mets, XDocument xMets, XElement parent, XElement fileSec, Stack<string> directoryLabels)
        {
            // We want to create MetsFileWrapper::PhysicalStructure (WorkingDirectories and WorkingFiles).
            // We can traverse the physical structmap, finding div type=Directory and div type=File
            // But we have a problem - if a directory has no files in it, we don't know the path of that 
            // directory. If it has grandchildren we can eventually populate it. But if not we will have
            // to rely on the AMD premis:originalName as the local path.
            foreach (var div in parent.Elements(XNames.MetsDiv))
            {
                var type = div.Attribute("TYPE")?.Value.ToLowerInvariant();
                var label = div.Attribute("LABEL")?.Value;
                if (type == "directory")
                {
                    if (string.IsNullOrEmpty(label))
                    {
                        throw new NotSupportedException("If a mets:div has type Directory, it must have a label");
                    }
                    directoryLabels.Push(label);
                    var admId = div.Attribute("ADMID")?.Value;
                    if (admId.HasText())
                    {
                        var amd = xMets.Descendants(XNames.MetsAmdSec).SingleOrDefault(t => t.Attribute("ID")!.Value == admId);
                        if (amd != null)
                        {
                            var originalName = amd.Descendants(XNames.PremisOriginalName).SingleOrDefault()?.Value;
                            if (originalName != null)
                            {
                                // Only in this scenario can we create a directory
                                var workingDirectory = mets.PhysicalStructure!.FindDirectory(originalName, true);
                                if (workingDirectory!.Name.IsNullOrWhiteSpace())
                                {
                                    var nameFromPath = originalName.GetSlug();
                                    var nameFromLabel = directoryLabels.Any() ? directoryLabels.Pop() : null;
                                    workingDirectory.Name = nameFromLabel ?? nameFromPath;
                                    workingDirectory.LocalPath = originalName;
                                }
                            }
                        }
                    }
                }

                // type may be Directory, we need to match them up to file paths
                // but there might not be any directories in the structmap, just implied by flocats.

                // build all the files first on one pass then re=parse to make directories?

                bool haveUsedAdmIdAlready = false;
                foreach (var fptr in div.Elements(XNames.MetsFptr))
                {
                    var admId = div.Attribute("ADMID")?.Value; 
                    // Goobi METS has the ADMID on the mets:div. But that means we can use it only once!
                    // Going to make an assumption for now that the first encountered mets:fptr is the one that gets the ADMID
                    // - this is true for Goobi at Wellcome. But in reality we'd need a stricter check than that.

                    var fileId = fptr.Attribute("FILEID")!.Value;
                    var fileEl = fileSec.Descendants(XNames.MetsFile).Single(f => f.Attribute("ID")!.Value == fileId);
                    var mimeType = fileEl.Attribute("MIMETYPE")?.Value;  // Archivematica does not have this, have to get it from PRONOM, even reverse lookup
                    var flocat = fileEl.Elements(XNames.MetsFLocat).Single().Attribute(XNames.XLinkHref)!.Value;
                    if (admId == null)
                    {
                        admId = fileEl.Attribute("ADMID")!.Value; // EPrints and Archivematica METS have ADMID on the mets:file
                        haveUsedAdmIdAlready = false;
                    }
                    string? digest = null;
                    if (!haveUsedAdmIdAlready)
                    {
                        var techMd = xMets.Descendants(XNames.MetsTechMD).SingleOrDefault(t => t.Attribute("ID")!.Value == admId);
                        if(techMd == null)
                        {
                            // Archivematica does it this way
                            techMd = xMets.Descendants(XNames.MetsAmdSec).SingleOrDefault(t => t.Attribute("ID")!.Value == admId);
                        }
                        var fixity = techMd.Descendants(XNames.PremisFixity).SingleOrDefault();
                        if (fixity != null)
                        {
                            var algorithm = fixity.Element(XNames.PremisMessageDigestAlgorithm)?.Value?.ToLowerInvariant().Replace("-", "");
                            if (algorithm == "sha256")
                            {
                                digest = fixity.Element(XNames.PremisMessageDigest)?.Value;
                            }
                        }
                        haveUsedAdmIdAlready = true;
                    }
                    var parts = flocat.Split('/');
                    if (string.IsNullOrEmpty(mimeType))
                    {
                        // In the real version, we would have got this from Siegfried for born-digital archives
                        // but we'd still be reading it from the METS file we made.
                        if (MimeTypes.TryGetMimeType(parts[^1], out var foundMimeType))
                        {
                            logger.LogWarning($"Content Type for {flocat} was deduced from file extension: {foundMimeType}");
                            mimeType = foundMimeType;
                        }
                    }
                    
                    
                    var file = new WorkingFile
                    {
                        ContentType = mimeType,
                        LocalPath = flocat,
                        Digest = digest,
                        Name = label ?? parts[^1]
                    };
                    mets.Files.Add(file);

                    // We only know the "on disk" paths of folders from file paths in flocat
                    // so if we have /folder1/folder2/folder3/file1 where folder2 has no immediate children, we never see it directly.
                    // But we might see it in mets:div in the structmap]]
                    if (parts.Length > 0)
                    {
                        int walkBack = parts.Length;
                        while (walkBack > 1)
                        {
                            var parentDirectory = string.Join('/', parts[..(walkBack-1)]);
                            var workingDirectory = mets.PhysicalStructure!.FindDirectory(parentDirectory, true);
                            if (workingDirectory!.Name.IsNullOrWhiteSpace())
                            {
                                var nameFromPath = parts[walkBack-2];
                                var nameFromLabel = directoryLabels.Any() ? directoryLabels.Pop() : null;
                                workingDirectory.Name = nameFromLabel ?? nameFromPath;
                                workingDirectory.LocalPath = parentDirectory;
                            }
                            walkBack--;
                        }
                    }

                }

                ProcessChildStructDivs(mets, xMets, div, fileSec, directoryLabels);
            }
        }


}