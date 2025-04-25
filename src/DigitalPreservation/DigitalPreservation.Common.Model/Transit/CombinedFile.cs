using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class CombinedFile(WorkingFile? fileInDeposit, WorkingFile? fileInMets, string? relativePath = null)
{
    public string? LocalPath
    {
        get
        {
            if (relativePath == null)
            {
                return FileInDeposit?.LocalPath ?? FileInMets?.LocalPath;
            }
            if (FileInDeposit == null)
            {
                return FileInMets?.LocalPath;
            }

            if (FileInDeposit.LocalPath.StartsWith($"{relativePath}/"))
            {
                return FileInDeposit.LocalPath.RemoveStart($"{relativePath}/");
            }
            // We're in the root of a BagIt 
            return "../" +  FileInDeposit.LocalPath;
        }
    }

    // Which do we want to be the first choice?
    public string? Name => FileInDeposit?.Name ?? FileInMets?.Name;
    public string? Digest => FileInDeposit?.Digest ?? FileInMets?.Digest;
    public long? Size => FileInDeposit?.Size ?? FileInMets?.Size;
    public string? ContentType => FileInDeposit?.ContentType ?? FileInMets?.ContentType;
    
    
    public WorkingFile? FileInDeposit { get; private set; } = fileInDeposit;
    public WorkingFile? FileInMets { get; private set; } = fileInMets;

    public void DeleteFileInDeposit()
    {
        FileInDeposit = null;
    }
    
    public void DeleteFileInMets()
    {
        FileInMets = null;
    }
    
    public FileMisMatch<string>? GetNameMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.Name, FileInMets.Name);
    }
    
    public FileMisMatch<string>? GetDigestMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.Digest, FileInMets.Digest);
    }
    
    public FileMisMatch<long?>? GetSizeMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<long?>(FileInDeposit.Size, FileInMets.Size);
    }
    
    public FileMisMatch<string>? GetContentTypeMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.ContentType, FileInMets.ContentType);
    }
    
    public Whereabouts Whereabouts
    {
        get
        {
            if (FileInDeposit is not null && FileInMets is not null)
            {
                return Whereabouts.Both;
            }

            if (FileInDeposit is not null)
            {
                if (relativePath.HasText() && !FileInDeposit.LocalPath.StartsWith(relativePath))
                {
                    return Whereabouts.Extra;
                }
                return Whereabouts.Deposit;
            }

            if (FileInMets is not null)
            {
                return Whereabouts.Mets;
            }

            return Whereabouts.Neither;
        }
    }

    public class FileMisMatch<T>(T? valueInDeposit, T? valueInMets)
    {
        public T? ValueInDeposit = valueInDeposit;
        public T? ValueInMets = valueInMets;
    }
    
}