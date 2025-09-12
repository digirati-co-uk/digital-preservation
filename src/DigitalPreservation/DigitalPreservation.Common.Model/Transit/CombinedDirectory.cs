using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class CombinedDirectory(WorkingDirectory? directoryInDeposit, WorkingDirectory? directoryInMets, string? relativePath = null)
{
    public string? LocalPath
    {
        get
        {
            if (relativePath == null)
            {
                return DirectoryInDeposit?.LocalPath ?? DirectoryInMets?.LocalPath;
            }
            if (DirectoryInDeposit == null)
            {
                return DirectoryInMets?.LocalPath;
            }

            if (DirectoryInDeposit.LocalPath == relativePath)
            {
                return "";
            }
            if (DirectoryInDeposit.LocalPath.StartsWith($"{relativePath}/"))
            {
                return DirectoryInDeposit.LocalPath.RemoveStart($"{relativePath}/");
            }
            // We're in the root of a BagIt - which should not actually contain any other folders, but...
            return "../" +  DirectoryInDeposit.LocalPath;
        }
    }

    public string? Name => DirectoryInDeposit?.Name ?? DirectoryInMets?.Name;

    public WorkingDirectory? DirectoryInDeposit { get; private set; } = directoryInDeposit;
    public WorkingDirectory? DirectoryInMets { get; private set; } = directoryInMets;

    public List<CombinedFile> Files { get; set; } = [];
    public List<CombinedDirectory> Directories { get; set; } = [];

    private void DeleteDirectoryInDeposit()
    {
        DirectoryInDeposit = null;
    }

    private void DeleteDirectoryInMets()
    {
        DirectoryInMets = null;
    }
    
    public bool? HaveSameName()
    {
        if (DirectoryInDeposit is null || DirectoryInMets is null)
        {
            return null;
        }
        
        return DirectoryInDeposit.Name!.Equals(DirectoryInMets.Name!);
    }

    public Whereabouts Whereabouts
    {
        get
        {
            if (DirectoryInDeposit is not null && DirectoryInMets is not null)
            {
                return Whereabouts.Both;
            }

            if (DirectoryInDeposit is not null)
            {
                if (relativePath.HasText() && !DirectoryInDeposit.LocalPath.StartsWith(relativePath))
                {
                    return Whereabouts.Extra;
                }
                return Whereabouts.Deposit;
            }

            if (DirectoryInMets is not null)
            {
                return Whereabouts.Mets;
            }

            return Whereabouts.Neither;
        }
    }
    
    
    public CombinedDirectory? FindDirectory(string? path)
    {
        return FindDirectoryInternal(path, (directory, part) => directory.LocalPath!.GetSlug() == part);
    }
    
    // public CombinedDirectory? FindDirectoryByUriSafeSlugs(string? path)
    // {
    //     return FindDirectoryInternal(path, (directory, part) => directory.LocalPath!.GetUriSafeSlug() == part);
    // }

    private CombinedDirectory? FindDirectoryInternal(string? path, Func<CombinedDirectory, string, bool> predicate)
    {
        if (path.IsNullOrWhiteSpace() || path == "/")
        {
            return this;
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directory = this;
        foreach (var part in parts)
        {
            var potentialDirectory = directory.Directories.SingleOrDefault(d => predicate(d, part));
            if (potentialDirectory == null)
            {
                return null;
            }
            directory = potentialDirectory;
        }

        return directory;
        
    }
    

    public int DescendantFileCount(int counter = 0)
    {
        counter+= Files.Count;
        foreach (var directory in Directories)
        {
            counter += directory.DescendantFileCount(counter);
        }
        return counter;
    }

    public CombinedFile? FindFile(string path)
    {
        var parent = FindDirectory(path.GetParent());
        var slug = path.GetSlug();
        return parent?.Files.SingleOrDefault(f => f.LocalPath!.GetSlug() == slug);
    }
    
    
    // public CombinedFile? FindFileByUriSafeSlugs(string path)
    // {
    //     var parent = FindDirectoryByUriSafeSlugs(path.GetParent());
    //     var slug = path.GetUriSafeSlug();
    //     return parent?.Files.SingleOrDefault(f => f.LocalPath!.GetUriSafeSlug() == slug);
    // }

    public bool RemoveFileFromDeposit(string path, string depositPath, bool trueIfNotFound)
    {
        return RemoveFileFromBranch(path, depositPath, trueIfNotFound, Branch.Deposit);
    }
    
    public bool RemoveFileFromMets(string path, string depositPath, bool trueIfNotFound)
    {
        return RemoveFileFromBranch(path, depositPath, trueIfNotFound, Branch.Mets);
    }
    
    public bool RemoveDirectoryFromDeposit(string path, string depositPath, bool trueIfNotFound)
    {
        return RemoveDirectoryFromBranch(path, depositPath, trueIfNotFound, Branch.Deposit);
    }
    
    public bool RemoveDirectoryFromMets(string path, string depositPath, bool trueIfNotFound)
    {
        return RemoveDirectoryFromBranch(path, depositPath, trueIfNotFound, Branch.Mets);
    }
    
    private bool RemoveFileFromBranch(string path, string depositPath, bool trueIfNotFound, Branch branch)
    {
        var combinedFile = FindFile(path);
        if (combinedFile is null)
        {
            return trueIfNotFound;
        }

        switch (branch)
        {
            case Branch.Deposit when combinedFile.FileInDeposit is null:
            case Branch.Mets when combinedFile.FileInMets is null:
                return trueIfNotFound;
        }
        
        var combinedParent = FindDirectory(combinedFile.LocalPath!.GetParent());
        var parentDirectoryInBranch = branch switch
        {
            Branch.Deposit => combinedParent?.DirectoryInDeposit,
            Branch.Mets => combinedParent?.DirectoryInMets,
            _ => null
        };

        if (combinedParent is null || parentDirectoryInBranch is null)
        {
            return false;
        }

        // remove the file from the correct files branch of the tree
        var removePath = branch == Branch.Deposit ? depositPath : path;
        var fileInBranch = parentDirectoryInBranch.Files.Single(f => f.LocalPath == removePath);
        var removedFromBranch = parentDirectoryInBranch.Files.Remove(fileInBranch);

        switch (branch)
        {
            case Branch.Deposit:
                combinedFile.DeleteFileInDeposit();
                break;
            case Branch.Mets:
                combinedFile.DeleteFileInMets();
                break;
        }

        WorkingFile? fileInOtherBranch = null;
        if (branch == Branch.Deposit)
        {
            fileInOtherBranch = combinedFile.FileInMets;
        }
        else if (branch == Branch.Mets)
        {
            fileInOtherBranch = combinedFile.FileInDeposit;
        }
        
        
        if (fileInOtherBranch is null)
        {
            // this combinedFile now has neither deposit nor mets, so it too should be removed
            combinedParent.Files.Remove(combinedFile);
        }
        return removedFromBranch;
    }
    
    
    private bool RemoveDirectoryFromBranch(string path, string depositPath, bool trueIfNotFound, Branch branch)
    {
        var combinedDirectory = FindDirectory(path);
        if (combinedDirectory is null)
        {
            return trueIfNotFound;
        }

        if (combinedDirectory.Files.Count > 0 || combinedDirectory.Directories.Count > 0)
        {
            return false;
        }

        switch (branch)
        {
            case Branch.Deposit when combinedDirectory.DirectoryInDeposit is null:
            case Branch.Mets when combinedDirectory.DirectoryInMets is null:
                return trueIfNotFound;
        }
        
        var combinedParent = FindDirectory(combinedDirectory.LocalPath!.GetParent());
        var parentDirectoryInBranch = branch switch
        {
            Branch.Deposit => combinedParent?.DirectoryInDeposit,
            Branch.Mets => combinedParent?.DirectoryInMets,
            _ => null
        };

        if (combinedParent is null || parentDirectoryInBranch is null)
        {
            return false;
        }

        // remove the directory from the correct directories branch of the tree
        var removePath = branch == Branch.Deposit ? depositPath : path;
        var directoryInBranch = parentDirectoryInBranch.Directories.Single(d => d.LocalPath == removePath);
        var removedFromBranch = parentDirectoryInBranch.Directories.Remove(directoryInBranch);

        switch (branch)
        {
            case Branch.Deposit:
                combinedDirectory.DeleteDirectoryInDeposit();
                break;
            case Branch.Mets:
                combinedDirectory.DeleteDirectoryInMets();
                break;
        }

        WorkingDirectory? directoryInOtherBranch = branch switch
        {
            Branch.Deposit => combinedDirectory.DirectoryInMets,
            Branch.Mets => combinedDirectory.DirectoryInDeposit,
            _ => null
        };

        if (directoryInOtherBranch is null)
        {
            // this combinedDirectory now has neither deposit nor mets, so it too should be removed
            combinedParent.Directories.Remove(combinedDirectory);
        }
        return removedFromBranch;
    }

    private enum Branch
    {
        Deposit,
        Mets
    }

    
    
    public (List<CombinedDirectory>, List<CombinedFile>) Flatten()
    {
        var directories = new List<CombinedDirectory>();
        var files = new List<CombinedFile>();
        FlattenInternal(directories, files, this);
        return (directories, files);
    }

    private static void FlattenInternal(
        List<CombinedDirectory> directories,
        List<CombinedFile> files,
        CombinedDirectory traverseDirectory)
    {
        foreach (var directory in traverseDirectory.Directories)
        {
            directories.Add(directory.CloneForFlatten());
            FlattenInternal(directories, files, directory);
        }
        files.AddRange(traverseDirectory.Files);
    }

    private CombinedDirectory CloneForFlatten()
    {
        return new CombinedDirectory(DirectoryInDeposit, DirectoryInMets, relativePath);
    }
    
    public Result<Container> ToContainer(Uri repositoryUri, Uri origin, string? metsXmlPath, List<Uri>? uris = null)
    {
        uris?.Add(repositoryUri);
        var container = new Container
        {
            Name = Name,
            Id = repositoryUri,
            Origin = origin
        };
        foreach (var combinedDirectory in Directories)
        {
            string slug = combinedDirectory.LocalPath!.GetSlug();
            var childResult = combinedDirectory.ToContainer(
                 repositoryUri.AppendEscapedSlug(slug.EscapeForUriNoHashes()), // For Fedora
                 origin.AppendEscapedSlug(slug.EscapeForUri()), // Regular S3 URI
                 metsXmlPath,
                 uris);
             if (childResult is { Success: true, Value: not null })
             {
                 container.Containers.Add(childResult.Value);
             }
             else
             {
                 return childResult;
             }
        }
        foreach (var combinedFile in Files)
        {
            var slug = combinedFile.LocalPath!.GetSlug();
            var binaryId = repositoryUri.AppendEscapedSlug(slug.EscapeForUriNoHashes());
            uris?.Add(binaryId);
            
            var size = combinedFile.GetSingleSize();
            if (size <= 0)
            {
                return Result.FailNotNull<Container>(ErrorCodes.BadRequest, 
                    $"File {combinedFile.LocalPath} has different sizes in deposit and mets or does not have a size at all");
            }
            
            var contentType = combinedFile.GetSingleContentType();
            if (contentType is null)
            {
                if (combinedFile.LocalPath == metsXmlPath)
                {
                    // It is OK for the METS file to have a discrepancy - we default it to application/xml,
                    // but it might have been uploaded as text/xml (for example)
                    contentType = combinedFile.FileInMets?.ContentType ?? combinedFile.FileInDeposit?.ContentType;
                }
                else
                {
                    return Result.FailNotNull<Container>(ErrorCodes.BadRequest, 
                        $"File {combinedFile.LocalPath} has different content types in deposit and mets - " + string.Join(", ", combinedFile.GetAllContentTypes()));
                }
            }

            var digests = combinedFile.GetDistinctDigests();
            if (digests.Count == 0)
            {
                return Result.FailNotNull<Container>(ErrorCodes.BadRequest, 
                    $"File {combinedFile.LocalPath} has no digest information in either METS, metadata or Deposit file attributes.");
            }

            if (digests.Count > 1)
            {
                return Result.FailNotNull<Container>(ErrorCodes.BadRequest, 
                    $"File {combinedFile.LocalPath} has different digests in deposit and mets");
            }

            var name = combinedFile.GetName();
            
            container.Binaries.Add(new Binary
            {
                Id = binaryId,
                Name = name, 
                ContentType = contentType,
                Digest = digests[0],
                Size = size,
                Origin = origin.AppendEscapedSlug(slug.EscapeForUri())  // We'll need to unescape this back to a key
            });
        }
        return Result.OkNotNull(container);
    }

    public FileSizeTotals GetSizeTotals()
    {
        var totals = new FileSizeTotals();
        AddBinariesToTotals(this, totals, false);
        return totals;
    }

    private void AddBinariesToTotals(CombinedDirectory combinedDirectory, FileSizeTotals totals, bool includeDirInCount = true)
    {
        if (includeDirInCount)
        {
            totals.TotalDirectoryCount++;
        }
        foreach (var combinedFile in combinedDirectory.Files)
        {
            if (combinedFile.LocalPath!.StartsWith("__"))
            {
                continue; // We need IStorage.DepositFileSystem but we'd have to reference Storage.Repository.Common
            }
            totals.TotalFileCount++;
            var addedToTotal = false;
            if (combinedFile.FileInDeposit != null)
            {
                var size = combinedFile.FileInDeposit.Size;
                if (size is null or <= 0)
                {
                    size = combinedFile.DepositFileFormatMetadata?.Size;
                }
                totals.TotalSizeInDeposit += size ?? 0;
                if (size is > 0)
                {
                    totals.TotalSize += size.Value;
                    addedToTotal = true;
                }
            }

            if (combinedFile.FileInMets != null)
            {
                var metsSize = combinedFile.FileInMets.Size ?? 0;
                totals.TotalSizeInMets += metsSize;
                if(!addedToTotal)
                {
                    totals.TotalSize += metsSize;
                }
            }
        }
        
        foreach (var childDirectory in combinedDirectory.Directories)
        {
            AddBinariesToTotals(childDirectory, totals);
        }
    }

    public List<string> GetMisMatches()
    {
        var mismatches = new List<string>();
        AddMisMatches(mismatches);
        return mismatches;
    }


    private void AddMisMatches(List<string> localPaths)
    {
        foreach (var combinedFile in Files)
        {
            if (combinedFile.MisMatches.Count != 0)
            {
                localPaths.Add(combinedFile.LocalPath!);
            }
        }

        foreach (var combinedDirectory in Directories)
        {
            combinedDirectory.AddMisMatches(localPaths);
        }
    }
}

public class FileSizeTotals
{
    public int TotalFileCount { get; set; } = 0;
    public int TotalDirectoryCount { get; set; } = 0;
    public long TotalSize { get; set; }
    public long TotalSizeInDeposit { get; set; }
    public long TotalSizeInMets { get; set; }
}
