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
        if (path.IsNullOrWhiteSpace() || path == "/")
        {
            return this;
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directory = this;
        foreach (var part in parts)
        {
            var potentialDirectory = directory.Directories.SingleOrDefault(d => d.LocalPath!.GetSlug() == part);
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

    public bool RemoveFileFromDeposit(string path, bool trueIfNotFound)
    {
        return RemoveFileFromBranch(path, trueIfNotFound, Branch.Deposit);
    }
    
    public bool RemoveFileFromMets(string path, bool trueIfNotFound)
    {
        return RemoveFileFromBranch(path, trueIfNotFound, Branch.Mets);
    }
    
    public bool RemoveDirectoryFromDeposit(string path, bool trueIfNotFound)
    {
        return RemoveDirectoryFromBranch(path, trueIfNotFound, Branch.Deposit);
    }
    
    public bool RemoveDirectoryFromMets(string path, bool trueIfNotFound)
    {
        return RemoveDirectoryFromBranch(path, trueIfNotFound, Branch.Mets);
    }
    
    private bool RemoveFileFromBranch(string path, bool trueIfNotFound, Branch branch)
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
        var fileInBranch = parentDirectoryInBranch.Files.Single(f => f.LocalPath == path);
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
    
    
    private bool RemoveDirectoryFromBranch(string path, bool trueIfNotFound, Branch branch)
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
        var directoryInBranch = parentDirectoryInBranch.Directories.Single(d => d.LocalPath == path);
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
}