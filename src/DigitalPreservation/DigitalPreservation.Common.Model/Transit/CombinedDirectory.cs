using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class CombinedDirectory(WorkingDirectory? directoryInDeposit, WorkingDirectory? directoryInMets)
{
    public string? LocalPath => DirectoryInDeposit?.LocalPath ?? DirectoryInMets?.LocalPath;
    public string? Name => DirectoryInDeposit?.Name ?? DirectoryInMets?.Name;

    public WorkingDirectory? DirectoryInDeposit { get; } = directoryInDeposit;
    public WorkingDirectory? DirectoryInMets { get; } = directoryInMets;

    public List<CombinedFile> Files { get; set; } = [];
    public List<CombinedDirectory> Directories { get; set; } = [];
    
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
}