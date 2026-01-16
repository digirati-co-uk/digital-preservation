using System.Xml.Linq;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;

namespace Storage.Repository.Common.Mets.StorageImpl;

public class FileSystemMetsLoader : IMetsLoader
{
    public Task<Uri?> FindMetsFile(Uri root)
    {
        if (root.Scheme != "file")
        {
            throw new NotSupportedException(root.Scheme + " not supported");
        }
        Uri? file = null;
        var dir = new DirectoryInfo(root.AbsolutePath);
            
        // Need to find the METS. Look for "mets.xml" by preference
        var firstXmlFile = dir.EnumerateFiles().FirstOrDefault(
            f => MetsUtils.IsMetsFile(f.Name.GetSlug(), true));
        if (firstXmlFile == null)
        {
            firstXmlFile = dir.EnumerateFiles().FirstOrDefault(
                f => MetsUtils.IsMetsFile(f.Name.GetSlug(), false));
        }

        if (firstXmlFile == null)
        {
            var childDirs = dir.GetDirectories();
            if (childDirs is [{ Name: FolderNames.BagItData }]) // one and one only child directory, called data
            {                
                firstXmlFile = childDirs[0].EnumerateFiles().FirstOrDefault(
                    f => MetsUtils.IsMetsFile(f.Name.GetSlug(), true));
                if (firstXmlFile == null)
                {
                    firstXmlFile = childDirs[0].EnumerateFiles().FirstOrDefault(
                        f => MetsUtils.IsMetsFile(f.Name.GetSlug(), false));
                }
            }
        }
        if (firstXmlFile != null)
        {
            file = new Uri(firstXmlFile.FullName);
        }

        return Task.FromResult(file);
    }
    
    
    public Task<WorkingFile?> LoadMetsFileAsWorkingFile(Uri file)
    {
        WorkingFile? metsFileAsWorkingFile = null;
        // This "find the METS file" logic is VERY basic and doesn't even look at the file.
        // But this is just for Proof of Concept.
        if (File.Exists(file.AbsolutePath))
        {
            var fi = new FileInfo(file.AbsolutePath);
            metsFileAsWorkingFile = new WorkingFile
            {
                ContentType = "application/xml",
                LocalPath = fi.Name, // because mets must be in the root
                Name = fi.Name,
                Digest = Checksum.Sha256FromFile(fi)?.ToLowerInvariant()
            };
        }

        return Task.FromResult(metsFileAsWorkingFile);
    }
    
    
    
    
    public Task<(XDocument?, string)> ExamineXml(Uri file, string? digest, bool parse)
    {
        XDocument? xDoc = null;
        var fileETag = digest ?? string.Empty;
        if (parse)
        {
            xDoc = XDocument.Load(file.LocalPath);
        }
        return Task.FromResult((xDoc, fileETag));
    }
}